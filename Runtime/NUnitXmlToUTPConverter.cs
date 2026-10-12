using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Unity.TestProtocol;
using Unity.TestProtocol.Messages;

namespace Unity.TestFramework.UTPReporter
{
    /// <summary>
    /// Converts the NUnit test events raised during a test run into the Unity Test Protocol messages
    /// that report them.
    /// </summary>
    public interface INUnitXmlToUTPConverter
    {
        /// <summary>
        /// Converts one NUnit test event into the messages that report it.
        /// </summary>
        /// <param name="testEventXml">The XML of a single NUnit test event.</param>
        /// <returns>
        /// The messages reporting the event, in the order they are to be emitted. Empty when the event
        /// needs no message.
        /// </returns>
        IEnumerable<Message> Convert(string testEventXml);
    }

    /// <summary>
    /// The default <see cref="INUnitXmlToUTPConverter"/>. An instance remembers the tests and suites it
    /// has already reported the start of, so it belongs to a single test run and is not thread-safe.
    /// </summary>
    public class NUnitXmlToUTPConverter : INUnitXmlToUTPConverter
    {
        private readonly HashSet<string> m_StartedTests = new HashSet<string>();
        private readonly HashSet<string> m_StartedGroups = new HashSet<string>();

        // Categories declared on the fixture are reported on the fixture node, not on the test nodes
        // inside it, and a test node carries no parentId to walk up. Keyed by the fixture's fullname:
        // a test node's classname is the class that declares the test rather than the fixture it ran
        // as, and a parameterized fixture's classname drops the arguments telling its instances apart.
        private readonly Dictionary<string, List<string>> m_FixtureCategories = new Dictionary<string, List<string>>();
        private static readonly Regex k_IsWhitespaceRegex = new Regex(@"^\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Converts one NUnit test event into the messages that report it.
        /// </summary>
        /// <param name="testEventXml">
        /// The XML of a single NUnit test event. Null, empty and whitespace-only input produce no
        /// messages.
        /// </param>
        /// <returns>
        /// The messages reporting the event, in the order they are to be emitted. An event carrying an
        /// outcome for a test whose start was never reported is preceded by that missing start, and a
        /// suite end whose start was never reported produces nothing.
        /// </returns>
        public IEnumerable<Message> Convert(string testEventXml)
        {
            var result = new List<Message>();
            if (string.IsNullOrEmpty(testEventXml) || k_IsWhitespaceRegex.IsMatch(testEventXml))
            {
                return result;
            }

            var doc = new XmlDocument();
            doc.LoadXml(testEventXml);

            var testEvent = doc.FirstChild;
           
            switch (testEvent.Name)
            {
                case "start-test":
                    result.Add(TestStarted(testEvent));
                    break;
                case "test-case":
                    if (testEvent.Attributes["label"] == null && testEvent.Attributes["result"] == null)
                    {
                        result.Add(TestStarted(testEvent));
                    }
                    else
                    {
                        result.AddRange(TestFinished(testEvent));
                    }
                   
                    break;
                case "test-output":
                    result.Add(ProcessTestOutput(testEvent));
                    break;
                case "start-suite":
                    RecordFixtureCategories(testEvent);
                    result.Add(ProcessSuiteStart(testEvent));
                    break;
                case "test-suite":
                    RecordFixtureCategories(testEvent);
                    result.Add(ProcessTestSuiteCompleted(testEvent));
                    break;
            }

            result.RemoveAll(x => x == null);
            return result;
        }

        private Message ProcessSuiteStart(XmlNode testEvent)
        {
            m_StartedGroups.Add(testEvent.Attributes["id"].Value);
            return TestGroupMessage.CreateGroupStart(testEvent.Attributes["fullname"].Value);
        }

        private Message ProcessTestSuiteCompleted(XmlNode testEvent)
        {
            var id = testEvent.Attributes["id"].Value;
            if (!m_StartedGroups.Contains(id))
            {
                return null;
            }

            m_StartedGroups.Remove(id);
            return TestGroupMessage.CreateGroupEnd(testEvent.Attributes["fullname"].Value);
        }

        private Message ProcessTestOutput(XmlNode testEvent)
        {
            var stream = testEvent.Attributes ? ["stream"];
            if (stream == null || stream.Value != "Out" && stream.Value != "Progress" && stream.Value != "Error")
            {
                return WarningMessage.Create($"Unexpected test-output event: {testEvent.OuterXml}");
            }

            if (testEvent.InnerText.StartsWith("##utp"))
            {
                return UnityTestProtocolMessageBuilder.Deserialize(testEvent.InnerText);
            }

            if (stream.Value == "Error")
            {
                return ErrorMessage.Create(testEvent.InnerText.TrimEnd());
            }
               
           return InfoMessage.Create(testEvent.InnerText.TrimEnd());
        }

        private Message TestStarted(XmlNode testEvent)
        {
            var testName = testEvent.Attributes["fullname"].Value;

            var id = testEvent.Attributes["id"].Value;
            m_StartedTests.Add(id);

            var message = TestStatusMessage.CreateTestStartMesssage(testName);

            // A consumer identifies a test from the phase it can see. An attempt that never reports an
            // End — the player died while it was running — is only ever seen through its Begin, so
            // identity the Begin omits is unrecoverable and the attempt cannot be matched to the test
            // it belongs to. NUnit already hands us all of this on this node.
            CopyAttribute(message, testEvent, "classname");
            CopyAttribute(message, testEvent, "methodname");
            AddCategories(message, testEvent);
            AddIteration(message, testEvent);

            return message;
        }

        private static void CopyAttribute(Message message, XmlNode testEvent, string attribute)
        {
            var value = testEvent.Attributes[attribute]?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                message[attribute] = value;
            }
        }

        private void RecordFixtureCategories(XmlNode suiteEvent)
        {
            var fixtureName = suiteEvent.Attributes["fullname"]?.Value;
            if (string.IsNullOrEmpty(fixtureName))
            {
                return;
            }

            var categories = ReadProperties(suiteEvent, "Category");
            if (categories.Count > 0)
            {
                m_FixtureCategories[fixtureName] = categories;
            }
        }

        private void AddCategories(Message message, XmlNode testEvent)
        {
            var categories = ReadProperties(testEvent, "Category");

            if (TryGetFixtureCategories(testEvent, out var inherited))
            {
                foreach (var category in inherited)
                {
                    if (!categories.Contains(category))
                    {
                        categories.Add(category);
                    }
                }
            }

            if (categories.Count > 0)
            {
                message["categories"] = categories;
            }
        }

        private bool TryGetFixtureCategories(XmlNode testEvent, out List<string> categories)
        {
            categories = null;

            // Once the fixture is known it is the only right answer: falling back to classname here
            // would hand an inherited test the categories of the base class it was declared in.
            if (TryGetFixtureName(testEvent, out var fixtureName))
            {
                return m_FixtureCategories.TryGetValue(fixtureName, out categories);
            }

            var className = testEvent.Attributes["classname"]?.Value;
            return !string.IsNullOrEmpty(className) && m_FixtureCategories.TryGetValue(className, out categories);
        }

        // Taking the name attribute off the end, rather than cutting at the last dot, keeps fixtures
        // whose tests carry arguments, and so dots, intact. A test can rename itself, which leaves name
        // no longer a suffix and the fixture unrecoverable -- what the caller's fallback is for.
        private static bool TryGetFixtureName(XmlNode testEvent, out string fixtureName)
        {
            fixtureName = null;

            var fullName = testEvent.Attributes["fullname"]?.Value;
            var name = testEvent.Attributes["name"]?.Value;
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(name)
                || fullName.Length <= name.Length + 1
                || !fullName.EndsWith("." + name, StringComparison.Ordinal))
            {
                return false;
            }

            fixtureName = fullName.Substring(0, fullName.Length - name.Length - 1);
            return true;
        }

        // Both counters are always present once either applies, so a repeat outranks a retry rather
        // than either winning by being non-null — the same precedence the editor reporter applies.
        private static void AddIteration(Message message, XmlNode testEvent)
        {
            var repeat = ReadIntProperty(testEvent, "repeatIteration");
            var retry = ReadIntProperty(testEvent, "retryIteration");

            message["iteration"] = repeat != 0 ? repeat : retry;
        }

        private static int ReadIntProperty(XmlNode node, string propertyName)
        {
            var value = FirstProperty(node, propertyName);
            return value != null
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static List<string> ReadProperties(XmlNode node, string propertyName)
        {
            var values = new List<string>();
            var properties = node.SelectNodes($"properties/property[@name='{propertyName}']");
            if (properties == null)
            {
                return values;
            }

            foreach (XmlNode property in properties)
            {
                var value = property.Attributes["value"]?.Value;
                if (!string.IsNullOrEmpty(value) && !values.Contains(value))
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static string FirstProperty(XmlNode node, string propertyName)
        {
            return node.SelectSingleNode($"properties/property[@name='{propertyName}']")?.Attributes["value"]?.Value;
        }

        private IEnumerable<Message> TestFinished(XmlNode testEvent)
        {
            var testName = testEvent.Attributes["fullname"].Value;
            var state = GetTestResultState(testEvent);
            var message = GetMessage(testEvent);
            var duration = GetDurationInMicroseconds(testEvent);
            var stackTrace = GetStackTrace(testEvent);
            var className = testEvent.Attributes["classname"]?.Value ?? string.Empty;

            var id = testEvent.Attributes["id"].Value;
            if (!m_StartedTests.Contains(id))
            {
                yield return TestStarted(testEvent);
            }

            var testInfo = new TestInfo(testName, state, message, duration, stackTrace, className);

            m_StartedTests.Remove(id);

            var endMessage = TestStatusMessage.CreateTestEndMesssage(testInfo);
            AddCategories(endMessage, testEvent);
            AddIteration(endMessage, testEvent);
            yield return endMessage;
        }

        private static string GetMessage(XmlNode testEvent)
        {
            var parentNode = testEvent.SelectSingleNode("failure")
                ?? testEvent.SelectSingleNode("reason")
                ?? testEvent.SelectSingleNode("assertion");

            if (parentNode != null)
            {
                var messageNode = parentNode.SelectSingleNode("message");
                return messageNode?.InnerText ?? string.Empty;
            }

            return testEvent.SelectSingleNode("output")?.InnerText ?? string.Empty;
        }

        private static TestStateEnum GetTestResultState(XmlNode testEvent)
        {
            var status = testEvent.Attributes["label"]?.Value ?? testEvent.Attributes["result"]?.Value ?? "";
            switch (status.ToLowerInvariant())
            {
                case "cancelled":
                    return TestStateEnum.Cancelled;
                case "error":
                    return TestStateEnum.Error;
                case "invalid":
                    return TestStateEnum.NotRunnable;
                case "failed":
                    return TestStateEnum.Failure;
                case "inconclusive":
                    return TestStateEnum.Inconclusive;
                case "passed":
                    return TestStateEnum.Success;
                case "ignored":
                    return TestStateEnum.Ignored;
                case "skipped":
                    return TestStateEnum.Skipped;
                case "explicit":
                    return TestStateEnum.Skipped;
                default:
                    throw new NotSupportedException($"The status '{status}' is not an implemented test status. Xml: '{testEvent.OuterXml}'");
            }
        }

        private static long GetDurationInMicroseconds(XmlNode testEvent)
        {
            var value = testEvent.Attributes["duration"]?.Value;
            var duration = value != null ? double.Parse(value, CultureInfo.InvariantCulture) : 0.0d; 
            return (long)Math.Round(duration * 1000000.0d);
        }

        private static string GetStackTrace(XmlNode testEvent)
        {
            var parentNode = testEvent.SelectSingleNode("failure")
                ?? testEvent.SelectSingleNode("assertion");
            var stackTraceNode = parentNode
                ?.SelectSingleNode("stack-trace");

            return stackTraceNode?.InnerText ?? string.Empty;
        }
    }
}
