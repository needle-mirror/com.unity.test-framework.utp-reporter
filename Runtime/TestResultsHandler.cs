using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework.Interfaces;
using Unity.TestProtocol;
using Unity.TestProtocol.Messages;
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
    /// <summary>
    /// Reports a player's test run as Unity Test Protocol messages. Outside the Editor the assembly
    /// registers this callback itself, so a player test run reports without any setup in the project
    /// under test. The transport is chosen from the player's own command line with
    /// <c>-utp-output-mode</c>, which takes <c>player-connection</c> (the default) or <c>log</c>.
    /// </summary>
    public class TestResultToUtpMessage : ITestRunCallback
    {
        private const string UtpOutputModeArgument = "-utp-output-mode";
        private const string UtpOutputModeLog = "log";
        private const string UtpOutputModePlayerConnection = "player-connection";
        private const string UtpPrefix = "##utp:";
        private readonly INUnitToUtpConverter m_Converter;
        private static readonly Guid UtpMessage = new Guid("28e419dab96b4e578a2717330f0e0b6f");
        private static readonly Guid TestRunFinishedMessageID = new Guid("8eb67a7f8faf49908e3d9a3ea8fab600");
        private UtpOutputMode m_OutputMode = UtpOutputMode.PlayerConnection;

        private enum UtpOutputMode
        {
            Log,
            PlayerConnection
        }

        /// <summary>
        /// Creates a reporter with the default converter.
        /// </summary>
        public TestResultToUtpMessage()
        {
            m_Converter = new NUnitToUtpConverter();
        }

        /// <summary>
        /// Reads the output mode from the command line and reports the start of the run.
        /// </summary>
        /// <param name="suiteStartedResult">The suite the run started with.</param>
        public void RunStarted(ITest suiteStartedResult)
        {
            m_OutputMode = ParseOutputMode(GetCommandLineArgs());
            Emit(m_Converter.RunStarted(suiteStartedResult));
        }

        /// <summary>
        /// Reports the end of the run. In <c>player-connection</c> mode the player signals completion
        /// and stays alive for the receiver to quit it, because payloads may still be in flight; in
        /// <c>log</c> mode there is no such handshake and the player quits itself.
        /// </summary>
        /// <param name="suiteFinishedResult">The result of the suite the run finished with.</param>
        public void RunFinished(ITestResult suiteFinishedResult)
        {
            Emit(m_Converter.RunFinished(suiteFinishedResult));

            if (m_OutputMode == UtpOutputMode.PlayerConnection)
            {
                // In player-connection mode we avoid quitting immediately because there can still be
                // buffered player-connection payloads in flight at run end. The receiver side is
                // expected to acknowledge completion by sending a quit message back once all data
                // has been consumed.
                GetPlayerConnection().Send(TestRunFinishedMessageID, new byte[1] { 1 });
                return;
            }

            // In log mode there is no player-connection completion handshake, so we terminate after
            // emitting final messages. This relies on Unity's normal orderly shutdown behavior to flush logs.
            ApplicationQuit();
        }

        /// <summary>
        /// Reports the start of a test.
        /// </summary>
        /// <param name="testStartedResult">The test that started.</param>
        public void TestStarted(ITest testStartedResult)
        {
            Emit(m_Converter.TestStarted(testStartedResult));
        }

        /// <summary>
        /// Reports the outcome of a test.
        /// </summary>
        /// <param name="testFinishedResult">The result of the test that finished.</param>
        public void TestFinished(ITestResult testFinishedResult)
        {
            Emit(m_Converter.TestFinished(testFinishedResult));
        }

        private void Emit(IEnumerable<Message> messages)
        {
            if (messages == null)
            {
                return;
            }

            foreach (var m in messages)
            {
                EmitSerializedMessage(UnityTestProtocolMessageBuilder.Serialize(m));
            }
        }

        /// <summary>
        /// Returns the connection messages are sent over in <c>player-connection</c> mode.
        /// </summary>
        /// <returns>The player connection to send on.</returns>
        protected virtual IEditorPlayerConnection GetPlayerConnection()
        {
            return PlayerConnection.instance;
        }

        /// <summary>
        /// Returns the command line the output mode is read from.
        /// </summary>
        /// <returns>The arguments the player was started with.</returns>
        protected virtual string[] GetCommandLineArgs()
        {
            return Environment.GetCommandLineArgs();
        }

        private void EmitSerializedMessage(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
            {
                return;
            }

            var payload = EnsureUtpPrefix(serialized);
            if (m_OutputMode == UtpOutputMode.Log)
            {
                EmitLog(payload);
                return;
            }

            var messageData = Encoding.UTF8.GetBytes(payload);
            GetPlayerConnection().Send(UtpMessage, messageData);
        }

        /// <summary>
        /// Writes a message to the player log in <c>log</c> mode.
        /// </summary>
        /// <param name="message">The serialized message, already carrying its prefix.</param>
        protected virtual void EmitLog(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
        }

        /// <summary>
        /// Quits the player once the run has been reported in <c>log</c> mode.
        /// </summary>
        protected virtual void ApplicationQuit()
        {
            Application.Quit();
        }

        private static string EnsureUtpPrefix(string serialized)
        {
            return serialized.StartsWith(UtpPrefix, StringComparison.Ordinal) ? serialized : UtpPrefix + serialized;
        }

        private static UtpOutputMode ParseOutputMode(string[] commandLineArgs)
        {
            if (commandLineArgs == null)
            {
                return UtpOutputMode.PlayerConnection;
            }

            for (var i = 0; i < commandLineArgs.Length - 1; i++)
            {
                if (!string.Equals(commandLineArgs[i], UtpOutputModeArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mode = commandLineArgs[i + 1];
                if (string.Equals(mode, UtpOutputModeLog, StringComparison.OrdinalIgnoreCase))
                {
                    return UtpOutputMode.Log;
                }

                if (string.Equals(mode, UtpOutputModePlayerConnection, StringComparison.OrdinalIgnoreCase))
                {
                    return UtpOutputMode.PlayerConnection;
                }

            }

            return UtpOutputMode.PlayerConnection;
        }
    }
}
