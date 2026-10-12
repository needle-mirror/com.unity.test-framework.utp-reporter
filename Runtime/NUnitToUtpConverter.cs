using System;
using System.Collections.Generic;
using NUnit.Framework.Interfaces;
using Unity.TestProtocol;
using Unity.TestProtocol.Messages;

namespace Unity.TestFramework.UTPReporter
{
    /// <summary>
    /// Converts the NUnit test events raised during a test run into the Unity Test Protocol messages
    /// that report them, reading the nodes the callback hands over rather than a serialized copy.
    /// </summary>
    public interface INUnitToUtpConverter
    {
        /// <summary>
        /// Reports the start of the run.
        /// </summary>
        /// <param name="test">The suite the run started with. Null produces no messages.</param>
        /// <returns>The messages reporting the event, in the order they are to be emitted.</returns>
        IEnumerable<Message> RunStarted(ITest test);

        /// <summary>
        /// Reports the end of the run.
        /// </summary>
        /// <param name="result">The result of the suite the run finished with. Null produces no messages.</param>
        /// <returns>The messages reporting the event, in the order they are to be emitted.</returns>
        IEnumerable<Message> RunFinished(ITestResult result);

        /// <summary>
        /// Reports the start of a test or of the suite containing one.
        /// </summary>
        /// <param name="test">The node that started. Null produces no messages.</param>
        /// <returns>
        /// The messages reporting the event, in the order they are to be emitted. Empty when the event
        /// needs no message, as for the run root, which is announced through
        /// <see cref="RunStarted"/> instead.
        /// </returns>
        IEnumerable<Message> TestStarted(ITest test);

        /// <summary>
        /// Reports the outcome of a test or of the suite containing one.
        /// </summary>
        /// <param name="result">The result of the node that finished. Null produces no messages.</param>
        /// <returns>
        /// The messages reporting the event, in the order they are to be emitted. Empty when the event
        /// needs no message, as for the run root, which is reported through
        /// <see cref="RunFinished"/> instead.
        /// </returns>
        IEnumerable<Message> TestFinished(ITestResult result);
    }

    // Builds UTP messages from the NUnit nodes the callback hands over, rather than from a
    // serialized copy of them. The tree is what carries the hierarchy: a node's parent is read off
    // ITest.Parent, so nothing depends on the order events happen to arrive in.
    /// <summary>
    /// The default <see cref="INUnitToUtpConverter"/>. An instance remembers the spans and attempt
    /// counts of the tests it has reported, so it belongs to a single test run and is not
    /// thread-safe.
    /// </summary>
    public class NUnitToUtpConverter : INUnitToUtpConverter
    {
        const string k_Category = "Category";
        const string k_RetryIteration = "retryIteration";
        const string k_RepeatIteration = "repeatIteration";

        static readonly Message[] k_NoMessages = new Message[0];

        // The run root is announced twice - once through RunStarted/RunFinished and again as an
        // ordinary TestStarted/TestFinished for the same node - so RunStarted owns that pair and the
        // test callbacks skip it. It is the only thing about groups worth remembering: every suite is
        // entered exactly once per run, so both phases recompute its span from its own key and
        // reconcile without an open set to keep in step.
        string m_RunRootKey;

        // Attempt index per test, counted here rather than read from NUnit's retryIteration, which
        // stays 0 for skipped and inconclusive re-runs and would make a Begin and its End disagree.
        readonly Dictionary<string, int> m_Iterations = new Dictionary<string, int>();

        // The span a Begin announced, reused by the matching End.
        readonly Dictionary<string, string> m_InFlight = new Dictionary<string, string>();

        /// <inheritdoc />
        public IEnumerable<Message> RunStarted(ITest test)
        {
            if (test == null)
            {
                return k_NoMessages;
            }

            m_RunRootKey = TestIdentity.UniqueName(test);
            return GroupStart(test);
        }

        /// <inheritdoc />
        public IEnumerable<Message> RunFinished(ITestResult result)
        {
            return GroupEnd(result?.Test);
        }

        /// <inheritdoc />
        public IEnumerable<Message> TestStarted(ITest test)
        {
            if (test == null || IsRunRoot(test))
            {
                return k_NoMessages;
            }

            return test.IsSuite ? GroupStart(test) : LeafStart(test);
        }

        /// <inheritdoc />
        public IEnumerable<Message> TestFinished(ITestResult result)
        {
            var test = result?.Test;
            if (test == null || IsRunRoot(test))
            {
                return k_NoMessages;
            }

            return test.IsSuite ? GroupEnd(test) : LeafEnd(result);
        }

        bool IsRunRoot(ITest test)
        {
            return m_RunRootKey != null && m_RunRootKey == TestIdentity.UniqueName(test);
        }

        IEnumerable<Message> GroupStart(ITest test)
        {
            if (test == null)
            {
                return k_NoMessages;
            }

            return Group(test, TestGroupMessage.CreateGroupStart(SuiteName(test)));
        }

        IEnumerable<Message> GroupEnd(ITest test)
        {
            if (test == null)
            {
                return k_NoMessages;
            }

            return Group(test, TestGroupMessage.CreateGroupEnd(SuiteName(test)));
        }

        static IEnumerable<Message> Group(ITest test, Message message)
        {
            ApplySpan(message, SpanIds.Create(TestIdentity.UniqueName(test)), ParentSpanId(test));
            return new[] { message };
        }

        List<Message> LeafStart(ITest test)
        {
            var messages = new List<Message>();
            var key = TestIdentity.UniqueName(test);

            m_Iterations.TryGetValue(key, out var attempt);
            m_Iterations[key] = attempt + 1;

            var spanId = SpanIds.Create(key, attempt);
            m_InFlight[key] = spanId;

            var message = TestStatusMessage.CreateTestStartMesssage(test.FullName);
            ApplySpan(message, spanId, ParentSpanId(test));
            Describe(message, test, key);
            messages.Add(message);
            return messages;
        }

        List<Message> LeafEnd(ITestResult result)
        {
            var test = result.Test;
            var key = TestIdentity.UniqueName(test);

            // An End whose Begin never arrived still needs one, or the consumer has no span to close.
            var messages = m_InFlight.ContainsKey(key) ? new List<Message>() : LeafStart(test);

            m_InFlight.TryGetValue(key, out var spanId);
            m_InFlight.Remove(key);

            var info = new TestInfo(
                test.FullName,
                State(result),
                ResultMessage(result),
                DurationMicroseconds(result),
                result.StackTrace ?? string.Empty,
                ClassName(test) ?? string.Empty);

            var message = TestStatusMessage.CreateTestEndMesssage(info);
            ApplySpan(message, spanId, ParentSpanId(test));
            Describe(message, test, key);
            messages.Add(message);
            return messages;
        }

        // Identity on both phases: an attempt cut off before its End is only ever seen through its
        // Begin, so whatever the Begin omits cannot be recovered.
        void Describe(Message message, ITest test, string uniqueName)
        {
            Set(message, "uniqueName", uniqueName);
            Set(message, "classname", ClassName(test));
            Set(message, "methodname", test.Method?.Name);

            var categories = Categories(test);
            if (categories.Count > 0)
            {
                message["categories"] = categories;
            }

            var iteration = ReportedIteration(test);
            if (iteration > 0)
            {
                message["iteration"] = iteration;
            }
        }

        // A parent is always a suite, entered once per run, so its span is recomputed from its own
        // key rather than tracked. Absent - not Guid.Empty - on the outermost node: absent is what
        // makes the consumer graft this subtree onto its own scope, where an explicit Empty would
        // be honoured as "I am a root" and detach it.
        static string ParentSpanId(ITest test)
        {
            var parent = test?.Parent;
            return parent == null ? null : SpanIds.Create(TestIdentity.UniqueName(parent));
        }

        static void ApplySpan(Message message, string spanId, string parentSpanId)
        {
            Set(message, "spanId", spanId);
            Set(message, "parentSpanId", parentSpanId);
        }

        static void Set(Message message, string field, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                message[field] = value;
            }
        }

        static string ClassName(ITest test)
        {
            return test?.TypeInfo?.FullName ?? test?.Parent?.TypeInfo?.FullName;
        }

        // Strip the directory when a suite is named by its dll path, so the consumer shows the
        // assembly name rather than an on-disk path.
        static string SuiteName(ITest test)
        {
            var name = test.FullName;
            if (string.IsNullOrEmpty(name) || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            var separator = name.LastIndexOfAny(new[] { '/', '\\' });
            return separator >= 0 ? name.Substring(separator + 1) : name;
        }

        // Categories declared on a fixture belong to the tests inside it, and a category can be set
        // at any level, so union the whole ancestor chain.
        static List<string> Categories(ITest test)
        {
            var categories = new List<string>();
            for (var node = test; node != null; node = node.Parent)
            {
                var values = Property(node, k_Category);
                if (values == null)
                {
                    continue;
                }

                foreach (var value in values)
                {
                    if (value is string category && !string.IsNullOrEmpty(category) && !categories.Contains(category))
                    {
                        categories.Add(category);
                    }
                }
            }

            return categories;
        }

        // Both counters are always present once either has fired, so a repeat outranks a retry
        // rather than either winning by being non-null.
        static int ReportedIteration(ITest test)
        {
            var repeat = IntProperty(test, k_RepeatIteration);
            return repeat != 0 ? repeat : IntProperty(test, k_RetryIteration);
        }

        static int IntProperty(ITest test, string name)
        {
            var values = Property(test, name);
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            return values[0] is int value ? value : 0;
        }

        // Never index a property bag for a key it may not have. NUnit's PropertyBag indexer inserts
        // an empty list on a miss, and UTF's own GetRetryIteration/GetRepeatIteration are written as
        // ContainsKey + .First() - so a read here plants a key that makes those throw and aborts the
        // whole run ("An unexpected error happened while running tests").
        static System.Collections.IList Property(ITest test, string name)
        {
            var properties = test?.Properties;
            return properties != null && properties.ContainsKey(name) ? properties[name] : null;
        }

        // A failure or a skip reason when there is one, and otherwise whatever the test wrote - which
        // is what the XML mapping reported, reading <failure>/<reason>/<assertion> and falling back
        // to <output>.
        static string ResultMessage(ITestResult result)
        {
            if (!string.IsNullOrEmpty(result.Message))
            {
                return result.Message;
            }

            return result.Output ?? string.Empty;
        }

        static long DurationMicroseconds(ITestResult result)
        {
            return (long)Math.Round(result.Duration * 1000000.0d);
        }

        // ResultState.Label is the same discriminator the XML carried in its "label" attribute, and
        // Status is what it carried in "result", so this reports what the previous XML mapping did -
        // except that an unrecognised state now falls through to Failure instead of throwing. The
        // dispatcher rethrows, so a throw here would fail the run rather than mislabel one test.
        static TestStateEnum State(ITestResult result)
        {
            var resultState = result?.ResultState;
            if (resultState == null)
            {
                return TestStateEnum.Inconclusive;
            }

            switch ((resultState.Label ?? string.Empty).ToLowerInvariant())
            {
                case "cancelled":
                    return TestStateEnum.Cancelled;
                case "error":
                    return TestStateEnum.Error;
                case "invalid":
                    return TestStateEnum.NotRunnable;
                case "ignored":
                    return TestStateEnum.Ignored;
                case "explicit":
                    return TestStateEnum.Skipped;
            }

            switch (resultState.Status)
            {
                case TestStatus.Passed:
                    return TestStateEnum.Success;
                case TestStatus.Skipped:
                    return TestStateEnum.Skipped;
                case TestStatus.Inconclusive:
                    return TestStateEnum.Inconclusive;
                default:
                    return TestStateEnum.Failure;
            }
        }
    }
}
