﻿namespace Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing
{
    using System;
    using System.Collections.Generic;
#if CORE_PCL || NET45 || WINRT || NET46
    using System.Diagnostics.Tracing;
#endif
#if NET35 || NET40
    using Microsoft.Diagnostics.Tracing;
#endif
#if WINDOWS_PHONE || WINDOWS_STORE
    using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
#else
    using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
    using Tracing;
    using Tracing.Mocks;

    [TestClass]
    public class DiagnosticsListenerTest
    {
        [TestMethod]
        public void TestConstructorThrowsArgumentException()
        {
            bool failedWithExpectedException = false;
            try
            {
                using (var listener = new DiagnosticsListener(null))
                {
                    // nop
                }
            }
            catch (ArgumentNullException)
            {
                failedWithExpectedException = true;
            }

            Assert.IsTrue(failedWithExpectedException);
        }

        [TestMethod]
        public void TestEventSending()
        {
            var senderMock = new F5DiagnosticsSenderMock();
            var senders = new List<IDiagnosticsSender> { senderMock };
            using (var listener = new DiagnosticsListener(senders))
            {
#if SILVERLIGHT
                CoreEventSource.Log.EnableEventListener(listener);
#endif
                listener.LogLevel = EventLevel.Verbose;
                CoreEventSource.Log.LogVerbose("failure");
            }

            Assert.AreEqual(1, senderMock.Messages.Count);
            Assert.AreEqual("[msg=Log verbose];[msg=failure]", senderMock.Messages[0]);
        }

        [TestMethod]
        public void TestListenerWithDifferentSeverity()
        {
            var senderMock = new F5DiagnosticsSenderMock();
            var senders = new List<IDiagnosticsSender> { senderMock };
            using (var listener = new DiagnosticsListener(senders))
            {
#if SILVERLIGHT
                CoreEventSource.Log.EnableEventListener(listener);
#endif
                listener.LogLevel = EventLevel.Informational;

                CoreEventSource.Log.LogVerbose("Some verbose tracing");
                Assert.AreEqual(0, senderMock.Messages.Count);

                CoreEventSource.Log.DiagnosticsEventThrottlingHasBeenResetForTheEvent(10, 1);
                Assert.AreEqual(1, senderMock.Messages.Count);

                senderMock.Messages.Clear();

                listener.LogLevel = EventLevel.Verbose;
                CoreEventSource.Log.LogVerbose("Some verbose tracing");
                Assert.AreEqual(1, senderMock.Messages.Count);

                senderMock.Messages.Clear();

                CoreEventSource.Log.DiagnosticsEventThrottlingHasBeenResetForTheEvent(10, 1);
                Assert.AreEqual(1, senderMock.Messages.Count);

                senderMock.Messages.Clear();
                
                listener.LogLevel = EventLevel.Error;
                CoreEventSource.Log.LogError("Logging an error");
                Assert.AreEqual(1, senderMock.Messages.Count);
            }
        }
    }
}
