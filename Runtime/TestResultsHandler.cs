using System;
using System.Text;
using System.Threading;
using NUnit.Framework.Interfaces;
using Unity.TestProtocol;
using UnityEngine;
using UnityEngine.TestRunner;
using UnityEngine.Networking.PlayerConnection;


#if !UNITY_EDITOR
using Unity.TestFramework.UTPReporter;
using UnityEngine.Scripting;
[assembly: Preserve]
[assembly: TestRunCallback(typeof(TestResultToUtpMessage))]
#endif

namespace Unity.TestFramework.UTPReporter
{
    public class TestResultToUtpMessage : ITestRunCallback
    {
        private readonly INUnitXmlToUTPConverter m_Converter;
        private static readonly Guid ApplicationQuitMessageId = new Guid("38a5d246506546dfaedb6653f6e22b33");
        private static readonly Guid UtpMessage = new Guid("28e419dab96b4e578a2717330f0e0b6f");
        private static readonly Guid TestRunFinishedMessageID = new Guid("8eb67a7f8faf49908e3d9a3ea8fab600");

        public TestResultToUtpMessage()
        {
            m_Converter = new NUnitXmlToUTPConverter();
        }

        public void RunStarted(ITest suiteStartedResult)
        {
            Send(suiteStartedResult);
        }

        public void RunFinished(ITestResult suiteFinishedResult)
        {
            Send(suiteFinishedResult);
            GetPlayerConnection().Send(TestRunFinishedMessageID, new byte[1] { 1 });
            if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                Thread.Sleep(1000);
                Application.Quit();
            }
        }

        public void TestStarted(ITest testStartedResult)
        {
            Send(testStartedResult);
        }

        public void TestFinished(ITestResult testFinishedResult)
        {
            Send(testFinishedResult);
        }

        private void Send(IXmlNodeBuilder xmlNodeBuilder)
        {
            var xml = xmlNodeBuilder.ToXml(recursive: false).OuterXml;
            var messages = m_Converter.Convert(xml);

            foreach (var m in messages)
            {
                var messageData = Encoding.UTF8.GetBytes(UnityTestProtocolMessageBuilder.Serialize(m));
                GetPlayerConnection().Send(UtpMessage, messageData);
            }
        }

        protected virtual IEditorPlayerConnection GetPlayerConnection()
        {
            return PlayerConnection.instance;
        }

        protected virtual void ApplicationQuit()
        {
            Application.Quit();
        }
    }
}
