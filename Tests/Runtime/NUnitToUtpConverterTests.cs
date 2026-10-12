using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using Unity.TestProtocol;
using Unity.TestProtocol.Messages;

namespace Unity.TestFramework.UTPReporter.Tests
{
    // Driven with the NUnit nodes the callback really hands over, not with serialized copies of
    // them: the tree is the input the converter reads, so a fixture that does not have one cannot
    // tell whether the parent walk, the identity key or the property reads are right.
    class NUnitToUtpConverterTests
    {
        NUnitToUtpConverter m_Converter;

        [SetUp]
        public void SetUp()
        {
            m_Converter = new NUnitToUtpConverter();
        }

        // ---- identity ------------------------------------------------------------------------

        [Test]
        public void TestStarted_ReportsIdentityOnTheBegin()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            var message = m_Converter.TestStarted(test).Single();

            Assert.That(message.IsTestStart());
            Assert.That(message.GetString("name"), Is.EqualTo(test.FullName));
            Assert.That(message.GetString("classname"), Is.EqualTo(typeof(Sample).FullName));
            Assert.That(message.GetString("methodname"), Is.EqualTo(nameof(Sample.Passing)));
            Assert.That(message.GetString("uniqueName"), Is.Not.Null.And.Not.Empty);
        }

        // uniqueName is the reason the XML round-trip had to go: it is not an NUnit XML attribute,
        // so it was unreachable from a serialized node however the converter was written.
        [Test]
        public void BeginAndEnd_ReportTheSameIdentity()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            var begin = m_Converter.TestStarted(test).Single();
            var end = m_Converter.TestFinished(Tree.Passed(test)).Single();

            Assert.That(end.GetString("uniqueName"), Is.EqualTo(begin.GetString("uniqueName")));
            Assert.That(end.GetString("classname"), Is.EqualTo(begin.GetString("classname")));
            Assert.That(end.GetString("methodname"), Is.EqualTo(begin.GetString("methodname")));
        }

        // The key is a reproduction of UnityEngine.TestRunner's GetUniqueName, which is internal
        // there. A tree with no TypeInfo anywhere pins the whole format in one assertion: ancestor
        // path, the name fallback, and the suite marker.
        [Test]
        public void UniqueName_ReproducesTheAncestorPathAndSuiteMarker()
        {
            var root = new TestSuite("Root");
            var middle = new TestSuite("Middle");
            var leaf = new TestSuite("Leaf");
            root.Add(middle);
            middle.Add(leaf);

            Assert.That(UniqueNameOf(root), Is.EqualTo("[Root][suite]"));
            Assert.That(UniqueNameOf(middle), Is.EqualTo("[Middle][suite]"));
            Assert.That(UniqueNameOf(leaf), Is.EqualTo("Middle/[Leaf][suite]"));
        }

        // Two tests can report the same name - [TestCase(TestName = ...)] in two fixtures - and the
        // consumer still has to tell them apart.
        [Test]
        public void UniqueName_DistinguishesSameNamedTestsInDifferentFixtures()
        {
            var first = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            var second = Tree.Method(Tree.Fixture<OtherSample>(), nameof(OtherSample.Passing));

            Assert.That(UniqueNameOf(first), Is.Not.EqualTo(UniqueNameOf(second)));
        }

        // ---- categories and iteration --------------------------------------------------------

        [Test]
        public void TestStarted_ReportsMethodLevelCategories()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            test.Properties.Add("Category", "Fast");

            var categories = Categories(m_Converter.TestStarted(test).Single());

            Assert.That(categories, Is.EqualTo(new[] { "Fast" }));
        }

        // The whole point of reading the tree: a category declared on the fixture belongs to the
        // tests inside it, and the previous converter had to remember it keyed on the class name
        // because a serialized test node carries no parent.
        [Test]
        public void TestStarted_UnionsCategoriesDeclaredUpTheTree()
        {
            var suite = new TestSuite("Namespace");
            suite.Properties.Add("Category", "SuiteLevel");
            var fixture = Tree.Fixture<Sample>(suite);
            fixture.Properties.Add("Category", "FixtureLevel");
            var test = Tree.Method(fixture, nameof(Sample.Passing));
            test.Properties.Add("Category", "MethodLevel");

            var categories = Categories(m_Converter.TestStarted(test).Single());

            Assert.That(categories, Is.EquivalentTo(new[] { "MethodLevel", "FixtureLevel", "SuiteLevel" }));
        }

        [Test]
        public void TestStarted_WhenNothingDeclaresACategory_OmitsTheField()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            Assert.That(m_Converter.TestStarted(test).Single().HasField("categories"), Is.False);
        }

        // UTR-1349: the fixture reports the class that ran and the test reports the class that
        // declares it, so keying inheritance on the class name dropped the fixture's categories for
        // every inherited test. The parent walk reads the fixture the test ran as.
        [Test]
        public void TestStarted_WhenTestIsDeclaredInABaseClass_InheritsCategoriesFromTheFixtureItRanAs()
        {
            var fixture = Tree.Fixture<DerivedSample>();
            fixture.Properties.Add("Category", "OnTheFixtureThatRan");
            var test = Tree.Method(fixture, nameof(BaseSample.Inherited));

            var categories = Categories(m_Converter.TestStarted(test).Single());

            Assert.That(categories, Is.EqualTo(new[] { "OnTheFixtureThatRan" }));
        }

        // The other half of the same defect: the declaring base class is not an ancestor of the test
        // that ran, so its own fixture's categories must not reach it however the tree is ordered.
        [Test]
        public void TestStarted_WhenTestIsInherited_DoesNotInheritTheDeclaringBaseClassesCategories()
        {
            var suite = new TestSuite("Namespace");
            var baseFixture = Tree.Fixture<BaseSample>(suite);
            baseFixture.Properties.Add("Category", "OnlyOnTheBaseFixture");
            var derivedFixture = Tree.Fixture<DerivedSample>(suite);
            var test = Tree.Method(derivedFixture, nameof(BaseSample.Inherited));

            Assert.That(m_Converter.TestStarted(test).Single().HasField("categories"), Is.False);
        }

        // Two fixtures of one type - what a parameterized fixture's instances are - share a class
        // name and nothing else. A test reads the instance it ran under, not whichever one declared
        // categories first.
        [Test]
        public void TestStarted_WhenFixtureIsParameterized_InheritsOnlyItsOwnParameterizationsCategories()
        {
            var suite = new TestSuite("Namespace");
            var categorized = Tree.Fixture<Sample>(suite);
            categorized.Properties.Add("Category", "OnTheOtherParameterization");
            var bare = Tree.Fixture<Sample>(suite);

            var fromBare = m_Converter.TestStarted(Tree.Method(bare, nameof(Sample.Passing))).Single();
            var fromCategorized = m_Converter.TestStarted(Tree.Method(categorized, nameof(Sample.Passing))).Single();

            Assert.That(fromBare.HasField("categories"), Is.False);
            Assert.That(Categories(fromCategorized), Is.EqualTo(new[] { "OnTheOtherParameterization" }));
        }

        [TestCase("retryIteration", 2, 2)]
        [TestCase("repeatIteration", 3, 3)]
        public void TestStarted_ReportsTheAttemptIndex(string property, int value, int expected)
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            test.Properties.Set(property, value);

            Assert.That(m_Converter.TestStarted(test).Single().GetInt("iteration"), Is.EqualTo(expected));
        }

        // Both counters exist once either has fired, so a repeat has to outrank a retry rather than
        // either winning by being the one that is present.
        [Test]
        public void TestStarted_PrefersRepeatIterationOverRetry()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            test.Properties.Set("retryIteration", 1);
            test.Properties.Set("repeatIteration", 4);

            Assert.That(m_Converter.TestStarted(test).Single().GetInt("iteration"), Is.EqualTo(4));
        }

        [Test]
        public void TestStarted_WhenNoAttemptHasHappened_OmitsTheField()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            Assert.That(m_Converter.TestStarted(test).Single().HasField("iteration"), Is.False);
        }

        // The defect that aborted a whole run: NUnit's PropertyBag indexer INSERTS an empty list for
        // a key it does not have, and UTF's GetRetryIteration/GetRepeatIteration are ContainsKey
        // followed by .OfType<int>().First(). So a read here with no apparent write leaves behind a
        // key that makes UTF throw "Sequence contains no elements" later, inside its own adaptor
        // factory - which is why no amount of asserting on the messages would have caught it.
        [Test]
        public void Converting_DoesNotPlantPropertyKeysOnTheTestItReads()
        {
            var fixture = Tree.Fixture<Sample>();
            var test = Tree.Method(fixture, nameof(Sample.Passing));

            m_Converter.TestStarted(test);
            m_Converter.TestFinished(Tree.Passed(test));

            foreach (var key in new[] { "Category", "retryIteration", "repeatIteration", "childIndex" })
            {
                Assert.That(test.Properties.ContainsKey(key), Is.False, $"test bag gained '{key}'");
                Assert.That(fixture.Properties.ContainsKey(key), Is.False, $"fixture bag gained '{key}'");
            }
        }

        // ---- spans and hierarchy ---------------------------------------------------------------

        // Absent, not Guid.Empty: absent is what makes the consumer graft this subtree onto its own
        // open scope, where an explicit all-zero id is honoured as "I am a root" and detaches it.
        [Test]
        public void RunStarted_OnANodeWithNoParent_OmitsTheParentSpanId()
        {
            var message = m_Converter.RunStarted(new TestSuite("Root")).Single();

            Assert.That(message.Is(TestGroupMessage.MessageType));
            Assert.That(message.IsBeginMessage());
            Assert.That(message.HasField("parentSpanId"), Is.False);
            Assert.That(message.GetString("spanId"), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void EveryNode_NamesTheSpanItsParentAnnouncedForItself()
        {
            var root = new TestSuite("Root");
            var fixture = Tree.Fixture<Sample>(root);
            var test = Tree.Method(fixture, nameof(Sample.Passing));

            var rootBegin = m_Converter.RunStarted(root).Single();
            var fixtureBegin = m_Converter.TestStarted(fixture).Single();
            var testBegin = m_Converter.TestStarted(test).Single();

            Assert.That(fixtureBegin.GetString("parentSpanId"), Is.EqualTo(rootBegin.GetString("spanId")));
            Assert.That(testBegin.GetString("parentSpanId"), Is.EqualTo(fixtureBegin.GetString("spanId")));
        }

        // A suite is entered once per run, so its span is recomputed from its own key on both
        // phases - there is no open-group set to keep the two in step.
        [Test]
        public void SuiteBeginAndEnd_ShareASpanWithoutAnythingBeingRemembered()
        {
            var root = new TestSuite("Root");
            var fixture = Tree.Fixture<Sample>(root);
            m_Converter.RunStarted(root);

            var begin = m_Converter.TestStarted(fixture).Single();
            var end = m_Converter.TestFinished(Tree.SuiteResult(fixture)).Single();

            Assert.That(end.IsEndMessage());
            Assert.That(end.GetString("spanId"), Is.EqualTo(begin.GetString("spanId")));
            Assert.That(end.GetString("parentSpanId"), Is.EqualTo(begin.GetString("parentSpanId")));
        }

        [Test]
        public void TestBeginAndEnd_ShareASpan()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            var begin = m_Converter.TestStarted(test).Single();
            var end = m_Converter.TestFinished(Tree.Passed(test)).Single();

            Assert.That(end.GetString("spanId"), Is.EqualTo(begin.GetString("spanId")));
        }

        // The attempt is counted here rather than read from NUnit's retryIteration, which stays 0
        // for skipped and inconclusive re-runs and would give two attempts the same span.
        [Test]
        public void ASecondAttempt_GetsItsOwnSpan()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            var first = m_Converter.TestStarted(test).Single();
            m_Converter.TestFinished(Tree.Passed(test));
            var second = m_Converter.TestStarted(test).Single();

            Assert.That(second.GetString("spanId"), Is.Not.EqualTo(first.GetString("spanId")));
        }

        // The run root arrives twice - once as RunStarted/RunFinished and again as an ordinary
        // test callback for the same node - and must still produce exactly one pair.
        [Test]
        public void TheRunRoot_IsAnnouncedOnceEvenThoughBothCallbacksCarryIt()
        {
            var root = new TestSuite("Root");

            var runBegin = m_Converter.RunStarted(root).Single();
            var duplicateBegin = m_Converter.TestStarted(root);
            var duplicateEnd = m_Converter.TestFinished(Tree.SuiteResult(root));
            var runEnd = m_Converter.RunFinished(Tree.SuiteResult(root)).Single();

            Assert.That(duplicateBegin, Is.Empty);
            Assert.That(duplicateEnd, Is.Empty);
            Assert.That(runEnd.IsEndMessage());
            Assert.That(runEnd.GetString("spanId"), Is.EqualTo(runBegin.GetString("spanId")));
        }

        // An End the consumer has no Begin for is a merge failure there, not a dropped message.
        [Test]
        public void TestFinished_WithNoPrecedingStart_EmitsTheBeginItNeeds()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));

            var messages = m_Converter.TestFinished(Tree.Passed(test)).ToList();

            Assert.That(messages.Count, Is.EqualTo(2));
            Assert.That(messages[0].IsTestStart());
            Assert.That(messages[1].IsTestFinish());
            Assert.That(messages[1].GetString("spanId"), Is.EqualTo(messages[0].GetString("spanId")));
        }

        // An assembly suite is named by its path on disk, and the consumer should show the assembly.
        [Test]
        public void ASuiteNamedByItsDllPath_IsReportedByFileName()
        {
            var suite = new TestSuite("Root");
            suite.FullName = "/Users/someone/project/Build/Sample.dll";

            Assert.That(m_Converter.RunStarted(suite).Single().GetString("name"), Is.EqualTo("Sample.dll"));
        }

        // ---- result mapping --------------------------------------------------------------------

        [Test]
        public void TestFinished_ReportsDurationInMicroseconds()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            var result = Tree.Passed(test);
            result.Duration = 1.5d;

            Assert.That(m_Converter.TestFinished(result).Last().GetLong("durationMicroseconds"), Is.EqualTo(1500000L));
        }

        [Test]
        public void TestFinished_ReportsTheFailureMessageAndStackTrace()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            var result = Tree.Result(test);
            result.SetResult(ResultState.Failure, "it broke", "at Sample.Passing()");

            var message = m_Converter.TestFinished(result).Last();

            Assert.That(message.GetString("message"), Is.EqualTo("it broke"));
            Assert.That(message.GetString("stackTrace"), Is.EqualTo("at Sample.Passing()"));
            Assert.That(message.GetInt("state"), Is.EqualTo((int)TestStateEnum.Failure));
        }

        // What the XML mapping did, and what a consumer showing a passing test's output relies on.
        [Test]
        public void TestFinished_WithNoFailureReason_ReportsTheCapturedOutput()
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            var result = Tree.Result(test);
            result.OutWriter.Write("hello");
            result.SetResult(ResultState.Success);

            Assert.That(m_Converter.TestFinished(result).Last().GetString("message"), Is.EqualTo("hello"));
        }

        static IEnumerable<TestCaseData> ResultStates()
        {
            yield return new TestCaseData(ResultState.Success, TestStateEnum.Success);
            yield return new TestCaseData(ResultState.Failure, TestStateEnum.Failure);
            yield return new TestCaseData(ResultState.Error, TestStateEnum.Error);
            yield return new TestCaseData(ResultState.Cancelled, TestStateEnum.Cancelled);
            yield return new TestCaseData(ResultState.NotRunnable, TestStateEnum.NotRunnable);
            yield return new TestCaseData(ResultState.Ignored, TestStateEnum.Ignored);
            yield return new TestCaseData(ResultState.Explicit, TestStateEnum.Skipped);
            yield return new TestCaseData(ResultState.Skipped, TestStateEnum.Skipped);
            yield return new TestCaseData(ResultState.Inconclusive, TestStateEnum.Inconclusive);
        }

        [TestCaseSource(nameof(ResultStates))]
        public void TestFinished_MapsTheResultState(ResultState state, TestStateEnum expected)
        {
            var test = Tree.Method(Tree.Fixture<Sample>(), nameof(Sample.Passing));
            var result = Tree.Result(test);
            result.SetResult(state);

            Assert.That(m_Converter.TestFinished(result).Last().GetInt("state"), Is.EqualTo((int)expected));
        }

        // ---- totality --------------------------------------------------------------------------
        // TestRunCallbackListener.InvokeAllCallbacks logs an exception and then RETHROWS it, so a
        // throw in here fails the test run rather than losing one message. Odd trees have to resolve
        // to a defined value instead.

        [Test]
        public void NullArguments_ProduceNoMessagesAndNoThrow()
        {
            Assert.That(m_Converter.RunStarted(null), Is.Empty);
            Assert.That(m_Converter.RunFinished(null), Is.Empty);
            Assert.That(m_Converter.TestStarted(null), Is.Empty);
            Assert.That(m_Converter.TestFinished(null), Is.Empty);
        }

        [Test]
        public void ASuiteWithNoTypeInfoNoParentAndNoChildren_IsStillReportable()
        {
            var suite = new TestSuite("Loose");

            var message = m_Converter.TestStarted(suite).Single();

            Assert.That(message.Is(TestGroupMessage.MessageType));
            Assert.That(message.HasField("parentSpanId"), Is.False);
            Assert.That(message.GetString("spanId"), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ASuiteWithNoTypeInfoOfItsOwn_TakesItFromItsFirstChild()
        {
            var suite = new TestSuite("Holder");
            suite.Add(Tree.Fixture<Sample>());

            Assert.That(UniqueNameOf(suite), Does.Contain(typeof(Sample).Assembly.GetName().Name));
        }

        // ---- helpers ---------------------------------------------------------------------------

        static string UniqueNameOf(ITest test)
        {
            return TestIdentity.UniqueName(test);
        }

        static List<string> Categories(Message message)
        {
            return (List<string>)message["categories"];
        }

        static class Tree
        {
            public static TestFixture Fixture<T>(TestSuite parent = null)
            {
                var fixture = new TestFixture(new TypeWrapper(typeof(T)));
                parent?.Add(fixture);
                return fixture;
            }

            public static TestMethod Method(TestFixture fixture, string name)
            {
                var test = new TestMethod(new MethodWrapper(fixture.TypeInfo.Type, name));
                fixture.Add(test);
                return test;
            }

            public static TestCaseResult Result(TestMethod test)
            {
                return new TestCaseResult(test);
            }

            public static TestCaseResult Passed(TestMethod test)
            {
                var result = new TestCaseResult(test);
                result.SetResult(ResultState.Success);
                return result;
            }

            public static TestSuiteResult SuiteResult(TestSuite suite)
            {
                var result = new TestSuiteResult(suite);
                result.SetResult(ResultState.Success);
                return result;
            }
        }

        class Sample
        {
            public void Passing() {}
        }

        class OtherSample
        {
            public void Passing() {}
        }

        class BaseSample
        {
            public void Inherited() {}
        }

        class DerivedSample : BaseSample
        {
        }
    }
}
