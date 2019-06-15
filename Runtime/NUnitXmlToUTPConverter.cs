using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Unity.TestProtocol;
using Unity.TestProtocol.Messages;

namespace Unity.TestFramework.UTPReporter
{
    public interface INUnitXmlToUTPConverter
    {
        IEnumerable<Message> Convert(string testEventXml);
    }

    public class NUnitXmlToUTPConverter : INUnitXmlToUTPConverter
    {
        private readonly HashSet<string> m_StartedTests = new HashSet<string>();
        private readonly HashSet<string> m_StartedGroups = new HashSet<string>();
        private static readonly Regex k_IsWhitespaceRegex = new Regex(@"^\s*$", RegexOptions.Compiled);

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
                    result.Add(ProcessSuiteStart(testEvent));
                    break;
                case "test-suite":
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

            return TestStatusMessage.CreateTestStartMesssage(testName);
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
            yield return TestStatusMessage.CreateTestEndMesssage(testInfo);
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
