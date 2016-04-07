﻿namespace Microsoft.HockeyApp.DataContracts
{
    using Microsoft.HockeyApp.Extensibility.Implementation;
    using Extensibility.Implementation.Tracing;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;


    /// <summary>
    /// Telemetry type used to track crashes.
    /// </summary>
    internal sealed partial class CrashTelemetry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CrashTelemetry"/> class.
        /// </summary>
        /// <param name="exception">The exception to initialize the class with.</param>
        public CrashTelemetry(Exception exception)
            : this()
        {
            if (exception == null)
            {
                exception = new Exception(Utils.PopulateRequiredStringValue(null, "message", typeof(ExceptionTelemetry).FullName));
            }

            this.InitializeFromException(exception);
        }

        /// <summary>
        /// Initializes the current instance with respect to passed in exception.
        /// </summary>
        /// <param name="exception">The exception to initialize the current instance with.</param>
        private void InitializeFromException(Exception exception)
        {
            this.Headers.Id = Guid.NewGuid().ToString("D");
            this.Headers.CrashThreadId = Environment.CurrentManagedThreadId;
            this.Headers.ExceptionType = exception.GetType().FullName;
            this.Headers.ExceptionReason = exception.Message;

            var description = string.Empty;
            if (TelemetryConfiguration.Active.DescriptionLoader != null)
            {
                try
                {
                    this.Attachments.Description = TelemetryConfiguration.Active.DescriptionLoader(exception);
                }
                catch (Exception ex)
                {
                    CoreEventSource.Log.LogError("HockeySDK: An exception occured in TelemetryConfiguration.Active.DescriptionLoader callback : " + ex);
                }
            }

            CrashTelemetryThread thread = new CrashTelemetryThread { Id = Environment.CurrentManagedThreadId };
            this.Threads.Add(thread);
            HashSet<long> seenBinaries = new HashSet<long>();

            StackTrace stackTrace = new StackTrace(exception, true);
            var frames = stackTrace.GetFrames();

            // stackTrace.GetFrames may return null (happened on Outlook Groups application). 
            // HasNativeImage() method invoke on first frame is required to understand whether an application is compiled in native tool chain
            // and we can extract the frame addresses or not.
            if (frames != null && frames.Length > 0 && frames[0].HasNativeImage())
            {
                foreach (StackFrame frame in stackTrace.GetFrames())
                {
                    CrashTelemetryThreadFrame crashFrame = new CrashTelemetryThreadFrame
                    {
                        Address = string.Format(CultureInfo.InvariantCulture, "0x{0:x16}", frame.GetNativeIP().ToInt64())
                    };
                    thread.Frames.Add(crashFrame);

                    long nativeImageBase = frame.GetNativeImageBase().ToInt64();
                    if (seenBinaries.Contains(nativeImageBase) == true)
                    {
                        continue;
                    }

                    PEImageReader reader = new PEImageReader(frame.GetNativeImageBase());
                    PEImageReader.CodeViewDebugData codeView = reader.Parse();
                    if (codeView == null)
                    {
                        continue;
                    }

                    CrashTelemetryBinary crashBinary = new CrashTelemetryBinary
                    {
                        StartAddress = string.Format(CultureInfo.InvariantCulture, "0x{0:x16}", nativeImageBase),
                        EndAddress = string.Format(CultureInfo.InvariantCulture, "0x{0:x16}", codeView.EndAddress.ToInt64()),
                        Uuid = string.Format(CultureInfo.InvariantCulture, "{0:N}-{1}", codeView.Signature, codeView.Age),
                        Path = codeView.PdbPath,
                        Name = string.IsNullOrEmpty(codeView.PdbPath) == false ? Path.GetFileNameWithoutExtension(codeView.PdbPath) : null,
                        CpuType = Extensibility.DeviceContextReader.GetProcessorArchitecture()
                    };

                    this.Binaries.Add(crashBinary);
                    seenBinaries.Add(nativeImageBase);
                }
            }

            this.StackTrace = RemoveSDKMethodsFromStackTrace(exception.StackTrace);
        }

        /// <summary>
        /// Removing SDK methods from the StackTrace. They appear on the stack, because of the implementation of 
        /// <see cref="Microsoft.HockeyApp.Extensibility.Windows.UnhandledExceptionTelemetryModule.CoreApplication_UnhandledErrorDetected(object, Windows.ApplicationModel.Core.UnhandledErrorDetectedEventArgs)"/>
        /// by using try..catch and UnhandledError.Propagate method.
        /// </summary>
        /// <param name="stackTrace">original <see cref="System.Exception.StackTrace"/>.</param>
        /// <returns><see cref="System.Exception.StackTrace"/> with removed SDK methods.</returns>
        private static string RemoveSDKMethodsFromStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return stackTrace;
            }

            string subStr = "\r\n   at Windows.ApplicationModel.Core.UnhandledError.Propagate()\r\n   at Microsoft.HockeyApp.Extensibility.Windows.UnhandledExceptionTelemetryModule.CoreApplication_UnhandledErrorDetected(Object sender, UnhandledErrorDetectedEventArgs e)";
            int i = stackTrace.LastIndexOf(subStr, StringComparison.Ordinal);
            if (i > 0)
            {
                stackTrace = stackTrace.Remove(i, subStr.Length);
            }

            return stackTrace;
        }
    }
}
