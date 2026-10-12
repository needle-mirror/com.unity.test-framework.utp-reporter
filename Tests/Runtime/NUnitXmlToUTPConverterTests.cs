using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.TestProtocol;
using Unity.TestProtocol.Messages;

namespace Unity.TestFramework.UTPReporter.Tests
{
    class NUnitXmlToUTPConverterTests
    {
        private NUnitXmlToUTPConverter m_Converter;

        [SetUp]
        public void SetUp()
        {
            m_Converter = new NUnitXmlToUTPConverter();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void GetUtpMessages_WhenReportIsEmpty_ReturnsNull(string report)
        {
            Assert.That(m_Converter.Convert(report), Is.Empty);
        }

        [Test]
        public void Convert_TestCaseWithoutLabelAndState_ReportTestStart()
        {
            var message = m_Converter.Convert("<test-case id=\"1005\" name=\"AddingSphereInHiddenPositionDoesNotFireStateChangeEvent\" fullname=\"CullingGroupTests.AddingSphereInHiddenPositionDoesNotFireStateChangeEvent\" methodname=\"AddingSphereInHiddenPositionDoesNotFireStateChangeEvent\" classname=\"CullingGroupTests\" runstate=\"Runnable\" seed=\"414006028\"><properties><property name=\"_JOINTYPE\" value=\"UnityCombinatorial\" /></properties></test-case>\r\n").Single();

            Assert.That(message.IsTestStart());
            Assert.That(message.data["name"], Is.EqualTo("CullingGroupTests.AddingSphereInHiddenPositionDoesNotFireStateChangeEvent"));
        }

        // An attempt that never reports an End is only ever seen through its Begin, so a consumer
        // cannot otherwise tell it apart from — or match it to — the test it belongs to.
        [Test]
        public void Convert_TestCaseWithoutResult_ReportsTestStartWithClassAndMethod()
        {
            var message = m_Converter.Convert(
                "<test-case id=\"1005\" name=\"Test\" fullname=\"Ns.Fixture.Test\" methodname=\"Test\" classname=\"Ns.Fixture\" runstate=\"Runnable\" />").Single();

            Assert.That(message.IsTestStart());
            Assert.That(message.data["classname"], Is.EqualTo("Ns.Fixture"));
            Assert.That(message.data["methodname"], Is.EqualTo("Test"));
        }

        [Test]
        public void Convert_TestStartAndTestFinish_ReportTheSameClassName()
        {
            var start = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Ns.Fixture.Test\" methodname=\"Test\" classname=\"Ns.Fixture\" />").Single();
            var end = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Ns.Fixture.Test\" classname=\"Ns.Fixture\" result=\"passed\" />").Single(x => x.IsTestFinish());

            Assert.That(start.data["classname"], Is.EqualTo(end.data["classname"]));
        }

        // start-test carries no class or method, so the fields must simply be absent rather than empty.
        [Test]
        public void Convert_WhenStartTestHasNoClassName_ThenFieldIsOmitted()
        {
            var message = m_Converter.Convert("<start-test id=\"1\" fullname=\"test name\" />").Single();

            Assert.That(message.IsTestStart());
            Assert.That(message.data.Contains("classname"), Is.False);
            Assert.That(message.data.Contains("methodname"), Is.False);
        }

        [Test]
        public void Convert_TestStart_ReportsMethodLevelCategories()
        {
            var message = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\">"
                + "<properties><property name=\"Category\" value=\"Fast\" /><property name=\"Retry\" value=\"3\" /></properties>"
                + "</test-case>").Single();

            Assert.That(message.IsTestStart());
            Assert.That((IEnumerable<string>)message.data["categories"], Is.EqualTo(new[] { "Fast" }));
        }

        // A fixture reports its categories on its own node, and the test nodes inside it carry neither
        // those categories nor a parentId to walk up, so they have to be remembered by name.
        [Test]
        public void Convert_TestStart_InheritsCategoriesFromItsFixture()
        {
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"10\" fullname=\"Fixture\" classname=\"Fixture\">"
                + "<properties><property name=\"Category\" value=\"Slow\" /></properties></test-suite>");

            var message = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\">"
                + "<properties><property name=\"Category\" value=\"Fast\" /></properties></test-case>").Single();

            Assert.That((IEnumerable<string>)message.data["categories"], Is.EquivalentTo(new[] { "Fast", "Slow" }));
        }

        [Test]
        public void Convert_TestStart_WhenTestIsDeclaredInABaseClass_InheritsCategoriesFromTheFixtureItRanAs()
        {
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"10\" fullname=\"Ns.DerivedFixture\" classname=\"Ns.DerivedFixture\">"
                + "<properties><property name=\"Category\" value=\"Slow\" /></properties></test-suite>");

            var message = m_Converter.Convert(
                "<test-case id=\"1\" name=\"Test\" fullname=\"Ns.DerivedFixture.Test\" classname=\"Ns.BaseFixture\" />").Single();

            Assert.That((IEnumerable<string>)message.data["categories"], Is.EqualTo(new[] { "Slow" }));
        }

        [Test]
        public void Convert_TestFinish_WhenTestIsDeclaredInABaseClass_InheritsCategoriesFromTheFixtureItRanAs()
        {
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"10\" fullname=\"Ns.DerivedFixture\" classname=\"Ns.DerivedFixture\">"
                + "<properties><property name=\"Category\" value=\"Slow\" /></properties></test-suite>");

            var message = m_Converter.Convert(
                "<test-case id=\"1\" name=\"Test\" fullname=\"Ns.DerivedFixture.Test\" classname=\"Ns.BaseFixture\" result=\"passed\" />")
                .Single(x => x.IsTestFinish());

            Assert.That((IEnumerable<string>)message.data["categories"], Is.EqualTo(new[] { "Slow" }));
        }

        // The base class a test is declared in can be a fixture in its own right, and its categories
        // belong to the tests that ran as it, not to those that merely inherited the method.
        [Test]
        public void Convert_TestStart_WhenTestIsInherited_DoesNotInheritTheDeclaringBaseClassesCategories()
        {
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"10\" fullname=\"Ns.BaseFixture\" classname=\"Ns.BaseFixture\">"
                + "<properties><property name=\"Category\" value=\"BaseOnly\" /></properties></test-suite>");
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"20\" fullname=\"Ns.DerivedFixture\" classname=\"Ns.DerivedFixture\" />");

            var message = m_Converter.Convert(
                "<test-case id=\"1\" name=\"Test\" fullname=\"Ns.DerivedFixture.Test\" classname=\"Ns.BaseFixture\" />").Single();

            Assert.That(message.data.Contains("categories"), Is.False);
        }

        // Only one parameterization declaring a category is the leaking case: nothing is recorded for the
        // other, so a lookup by the classname they share would find the first one's categories.
        [Test]
        public void Convert_TestStart_WhenOnlyAnotherParameterizationHasCategories_InheritsNone()
        {
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"10\" fullname=\"Ns.Fixture(1)\" classname=\"Ns.Fixture\">"
                + "<properties><property name=\"Category\" value=\"First\" /></properties></test-suite>");
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"20\" fullname=\"Ns.Fixture(2)\" classname=\"Ns.Fixture\" />");

            var message = m_Converter.Convert(
                "<test-case id=\"1\" name=\"Test\" fullname=\"Ns.Fixture(2).Test\" classname=\"Ns.Fixture\" />").Single();

            Assert.That(message.data.Contains("categories"), Is.False);
        }

        [Test]
        public void Convert_TestStart_WhenFixtureIsParameterized_InheritsOnlyItsOwnParameterizationsCategories()
        {
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"10\" fullname=\"Ns.Fixture(1)\" classname=\"Ns.Fixture\">"
                + "<properties><property name=\"Category\" value=\"First\" /></properties></test-suite>");
            m_Converter.Convert(
                "<test-suite type=\"TestFixture\" id=\"20\" fullname=\"Ns.Fixture(2)\" classname=\"Ns.Fixture\">"
                + "<properties><property name=\"Category\" value=\"Second\" /></properties></test-suite>");

            var message = m_Converter.Convert(
                "<test-case id=\"1\" name=\"Test\" fullname=\"Ns.Fixture(1).Test\" classname=\"Ns.Fixture\" />").Single();

            Assert.That((IEnumerable<string>)message.data["categories"], Is.EqualTo(new[] { "First" }));
        }

        [Test]
        public void Convert_WhenTestHasNoCategories_ThenFieldIsOmitted()
        {
            var message = m_Converter.Convert("<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\" />").Single();

            Assert.That(message.data.Contains("categories"), Is.False);
        }

        // The attempt index is the runner's own count, so a consumer should not have to infer it from
        // the shape of the results.
        [Test]
        public void Convert_TestFinish_ReportsRetryIteration()
        {
            var message = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\" result=\"passed\">"
                + "<properties><property name=\"retryIteration\" value=\"2\" /><property name=\"repeatIteration\" value=\"0\" /></properties>"
                + "</test-case>").Single(x => x.IsTestFinish());

            Assert.That(message.data["iteration"], Is.EqualTo(2));
        }

        [Test]
        public void Convert_TestFinish_PrefersRepeatIterationOverRetry()
        {
            var message = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\" result=\"passed\">"
                + "<properties><property name=\"retryIteration\" value=\"0\" /><property name=\"repeatIteration\" value=\"4\" /></properties>"
                + "</test-case>").Single(x => x.IsTestFinish());

            Assert.That(message.data["iteration"], Is.EqualTo(4));
        }

        // Retry sets the property on the node it emits for the next attempt, so the Begin of an
        // attempt carries the index too — the only phase a cut-off attempt is ever seen through.
        [Test]
        public void Convert_TestStart_ReportsRetryIteration()
        {
            var message = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\">"
                + "<properties><property name=\"retryIteration\" value=\"2\" /></properties>"
                + "</test-case>").Single(x => x.IsTestStart());

            Assert.That(message.data["iteration"], Is.EqualTo(2));
        }

        // The field is a non-nullable int on the consumer, so an omitted key and a zero are the same
        // value there; reporting it keeps this producer's wire identical to the editor reporter's.
        [TestCase("<properties><property name=\"retryIteration\" value=\"0\" /></properties>")]
        [TestCase("")]
        public void Convert_WhenNoAttemptHasHappened_ThenIterationIsZero(string properties)
        {
            var message = m_Converter.Convert(
                "<test-case id=\"1\" fullname=\"Fixture.Test\" classname=\"Fixture\" result=\"passed\">"
                + properties
                + "</test-case>").Single(x => x.IsTestFinish());

            Assert.That(message.data["iteration"], Is.EqualTo(0));
        }

        [Test]
        public void Convert_WhenStartTestContainsFullName_ThenMessageContainsName()
        {
            var message = m_Converter.Convert("<start-test id=\"1\" fullname=\"test name\" />").Single();

            Assert.That(message.IsTestStart());
            Assert.That(message.data["name"], Is.EqualTo("test name"));
        }

        [Test]
        public void Convert_WhenTestCaseContainsFullName_ThenMessageContainsFullName()
        {
            var message = m_Converter.Convert("<test-case id=\"1\" fullname=\"test name\" result=\"passed\" />").Single(x => x.IsTestFinish());

            Assert.That(message.IsTestFinish());
            Assert.That(message.data["name"], Is.EqualTo("test name"));
        }

        [Test]
        public void Convert_WhenTestCaseContainsClassName_ThenMessageContainsClassName()
        {
            var message = m_Converter.Convert("<test-case id=\"1\" fullname=\"foo\" classname=\"Foo\" result=\"passed\" />").Single(x => x.IsTestFinish());

            Assert.That(message.data["classname"], Is.EqualTo("Foo"));
        }

        [TestCase("0", 0)]
        [TestCase("0.0", 0)]
        [TestCase("0.00", 0)]
        [TestCase("0.000", 0)]
        [TestCase("0.0000", 0)]
        [TestCase("0.00000", 0)]
        [TestCase("0.000000", 0)]
        [TestCase("0.000001", 1)]
        [TestCase("0.000012", 12)]
        [TestCase("0.000123", 123)]
        [TestCase("0.001234", 1234)]
        [TestCase("0.012345", 12345)]
        [TestCase("0.123456", 123456)]
        [TestCase("1.234567", 1234567)]
        [TestCase("1.23456723", 1234567)]
        public void Convert_WhenTestCaseContainsDuration_ThenMessageContainsDurationInMicroseconds(
            string duration, long expectedDuration)
        {
            var report = $"<test-case id=\"1\" fullname=\"foo\" result=\"passed\" duration=\"{duration}\" />";

            var message = m_Converter.Convert(report).Single(x => x.IsTestFinish());

            Assert.That(message.data["durationMicroseconds"], Is.EqualTo(expectedDuration));
        }

        [TestCase("Failed", "Cancelled", TestStateEnum.Cancelled)]
        [TestCase("Failed", "Error", TestStateEnum.Error)]
        [TestCase("Failed", "Invalid", TestStateEnum.NotRunnable)]
        [TestCase("Failed", null, TestStateEnum.Failure)]
        [TestCase("Inconclusive", null, TestStateEnum.Inconclusive)]
        [TestCase("Passed", null, TestStateEnum.Success)]
        [TestCase("Skipped", "Ignored", TestStateEnum.Ignored)]
        [TestCase("Skipped", null, TestStateEnum.Skipped)]
        public void Convert_WhenTestCaseContainsResultAndLabel_ThenMessageContainsExpectedState(
            string result, string label, TestStateEnum state)
        {
            var resultAttribute = result != null ? $"result=\"{result}\"" : null;
            var labelAttribute = label != null ? $"label=\"{label}\"" : null;
            var report = $"<test-case id=\"1\" fullname=\"foo\" {resultAttribute} {labelAttribute} />";

            var message = m_Converter.Convert(report).Single(x => x.IsTestFinish());

            Assert.That(message, Is.Not.Null);
            Assert.That(message.IsTestFinish());
            Assert.That(message.data["state"], Is.EqualTo((int)state));
        }

        [Test]
        public void Convert_WhenTestCaseContainsOutput_ThenMessageContainsOutputInMessage()
        {
            var report = "<test-case id=\"1\" fullname=\"foo\" result=\"Failed\">"
                + "<output>"
                + "<![CDATA[foo bar baz]]>"
                + "</output>"
                + "</test-case>";

            var message = m_Converter.Convert(report).Single(x => x.IsTestFinish());

            Assert.That(message.data["message"], Is.EqualTo("foo bar baz"));
        }

        [TestCase("failure")]
        [TestCase("reason")]
        [TestCase("assertion")]
        public void Convert_WhenTestCaseContainsMessage_ThenMessageContainsMessage(string parentNodeType)
        {
            var report = "<test-case id=\"1\" fullname=\"foo\" result=\"Failed\">"
                + $"<{parentNodeType}>"
                + "<message>"
                + "<![CDATA[foo bar baz]]>"
                + "</message>"
                + $"</{parentNodeType}>"
                + "</test-case>";

            var message = m_Converter.Convert(report).Single(x => x.IsTestFinish());

            Assert.That(message.data["message"], Is.EqualTo("foo bar baz"));
        }

        [TestCase("failure")]
        [TestCase("assertion")]
        public void Convert_WhenEventContainsFailureWithStackTrace_ThenMessageWithStacktraceIsProduced(string parentNodeType)
        {
            var report = "<test-case id=\"1\" fullname=\"foo\" result=\"Failed\">"
                + $"<{parentNodeType}>"
                + "<stack-trace>"
                + "<![CDATA[some stacktrace message]]>"
                + "</stack-trace>"
                + $"</{parentNodeType}>"
                + "</test-case>";

            var message = m_Converter.Convert(report).Single(x => x.IsTestFinish());

            Assert.That(message.data["stackTrace"], Is.EqualTo("some stacktrace message"));
        }

        [TestCase("Skipped", "Ignored")]
        [TestCase("Skipped", null)]
        public void Convert_WhenTestCaseIsSkippedOrIgnored_ThenStartAndEndMessageIsCreated(
            string result, string label)
        {
            var resultAttribute = result != null ? string.Format("result=\"{0}\"", result) : string.Empty;
            var labelAttribute = label != null ? string.Format("label=\"{0}\"", label) : string.Empty;
            var report = $"<test-case id=\"1\" fullname=\"foo\" {resultAttribute} {labelAttribute} />";

            var messages = m_Converter.Convert(report);

            Assert.That(messages.Select(m => m.phase).ToArray(),
                Is.EqualTo(new[]
                {
                        Message.BeginPhase,
                        Message.EndPhase
                }));
        }

        [Test]
        public void Convert_WhenRunningMultipleEvents_ThenMessagesForThoseEventsAreReceived()
        {
            var allMessages = new List<Message>();
            allMessages.AddRange(m_Converter.Convert("<start-test id=\"1\" fullname=\"foo1\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"1\" fullname=\"foo1\" result=\"Passed\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"2\" fullname=\"foo2\" result=\"Skipped\" />"));
            allMessages.AddRange(m_Converter.Convert("<start-test id=\"3\" fullname=\"foo3\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"3\" fullname=\"foo3\" result=\"Failed\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"4\" fullname=\"foo4\" result=\"Skipped\" label=\"Ignored\"/>"));
            allMessages.AddRange(m_Converter.Convert("<start-test id=\"5\" fullname=\"foo5\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"5\" fullname=\"foo5\" result=\"Skipped\" />"));
            allMessages.AddRange(m_Converter.Convert("<start-test id=\"6\" fullname=\"foo6\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"6\" fullname=\"foo6\" result=\"Skipped\" label=\"Ignored\"/>"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"7\" fullname=\"foo7\" result=\"Skipped\" />"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"8\" fullname=\"foo8\" result=\"Skipped\" label=\"Ignored\"/>"));
            allMessages.AddRange(m_Converter.Convert("<test-case id=\"9\" fullname=\"foo9\" result=\"Skipped\" label=\"Explicit\"/>"));

            Assert.That(allMessages.Select(
                m => new object[] { m.phase, m.data["state"] != null ? (TestStateEnum)m.data["state"] : TestStateEnum.Inconclusive }).ToArray(),
                Is.EqualTo(new[]
                {
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Success},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Skipped},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Failure},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Ignored},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Skipped},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Ignored},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Skipped},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Ignored},
                        new object[] {Message.BeginPhase, TestStateEnum.Inconclusive},
                        new object[] {Message.EndPhase, TestStateEnum.Skipped},
                }));
        }

        [Test, TestCaseSource(typeof(OutputTestCases))]
        public void Convert_TestOutputIsGiven_LogsCorrespondingUtpMessages(string streamType, string text, string expectedUtpMessageType)
        {
            var xml = $"<test-output stream = \"{streamType}\" testname=\"testName\">{text}</test-output>";

            var message = m_Converter.Convert(xml).First();

            Assert.That(message.Is(expectedUtpMessageType));
            Assert.That(message["message"], Is.EqualTo(text.Trim()));
        }

        [TestCase("<test-output testname=\"testName\"></test-output>")]
        [TestCase("<test-output stream = \"Foo\" testname=\"testName\"></test-output>")]
        public void Convert_UnexpectedEventIsGiven_LogsWarning(string xml)
        {
            var message = m_Converter.Convert(xml).First();

            Assert.That(message.Is(WarningMessage.MessageType));
            Assert.That(message["message"], Contains.Substring("<test-output"));
        }

        [TestCase("Out")]
        [TestCase("Progress")]
        [TestCase("Error")]
        public void Convert_TestOutputContainsUtpMessage_LogsItAsItIs(string streamType)
        {
            var messageText = UnityTestProtocolMessageBuilder.Serialize(new Message("dummy"));
            var xml = $"<test-output stream = \"{streamType}\" testname=\"testName\">{messageText}</test-output>";

            var message = m_Converter.Convert(xml).First();

            Assert.That(message.Is("dummy"));
        }

        [Test]
        public void Convert_StartSuite_LogsCorrespondingUtpMessage()
        {
            var xml = "<start-suite id=\"0-3011\" parentId=\"0-123\" name=\"name\" fullname=\"fullName\" />";

            var message = m_Converter.Convert(xml).Single();

            Assert.That(message.Is(TestGroupMessage.MessageType));
        }

        [Test]
        public void Convert_CompetedSuite_ReturnsCorrespondingUtpMessage()
        {
            var suiteStart = "<start-suite id=\"0-3011\" parentId=\"0-123\" name=\"name\" fullname=\"fullName\" />";
            var suiteComplete = "<test-suite id=\"0-3011\" name=\"name\" fullname=\"fullName\" />";

            List<Message> allMessages = new List<Message>();
            allMessages.AddRange(m_Converter.Convert(suiteStart));
            allMessages.AddRange(m_Converter.Convert(suiteComplete));


            Assert.That(allMessages.First().Is(TestGroupMessage.MessageType));
            Assert.That(allMessages.First().IsBeginMessage);
            Assert.That(allMessages.Last().Is(TestGroupMessage.MessageType));
            Assert.That(allMessages.Last().IsEndMessage());
        }

        [Test]
        public void Convert_SuiteCompleteWithoutCorrespondingSuiteSTart_DoesNotReturnSuiteComplete()
        {
            var suiteStart = "<start-suite id=\"0-0000\" parentId=\"0-123\" name=\"name\" fullname=\"fullName\" />";
            var suiteComplete = "<test-suite id=\"0-1111\" name=\"name\" fullname=\"fullName\" />";

            var allMessages = new List<Message>();
            allMessages.AddRange(m_Converter.Convert(suiteStart));
            allMessages.AddRange(m_Converter.Convert(suiteComplete));

            Assert.That(allMessages.Count, Is.EqualTo(1));
            Assert.That(allMessages.First().Is(TestGroupMessage.MessageType));
            Assert.That(allMessages.First().IsBeginMessage());
        }

        private class OutputTestCase : TestCaseData
        {
            public OutputTestCase(string streamType, string text, string expectedUtpMessageType) :
                base(streamType, text, expectedUtpMessageType)
            {
            }
        }

        public class OutputTestCases : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                return TestCases.GetEnumerator();
            }

            public static IEnumerable TestCases
            {
                get
                {
                    yield return new OutputTestCase("Out", "text" + Environment.NewLine, InfoMessage.MessageType);
                    yield return new OutputTestCase("Progress", "text" + Environment.NewLine, InfoMessage.MessageType);
                    yield return new OutputTestCase("Error", "text" + Environment.NewLine, ErrorMessage.MessageType);
                }
            }
        }
    }
}

