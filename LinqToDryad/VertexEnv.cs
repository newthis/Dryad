/*
Copyright (c) Microsoft Corporation

All rights reserved.

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in 
compliance with the License.  You may obtain a copy of the License 
at http://www.apache.org/licenses/LICENSE-2.0   


THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER 
EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF 
TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT.  


See the Apache Version 2.0 License for specific language governing permissions and 
limitations under the License. 

*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Diagnostics;
using Microsoft.Research.DryadLinq;

namespace Microsoft.Research.DryadLinq.Internal
{
    /// <summary>
    /// Exposes the execution environment for managed vertex code.
    /// </summary>
    /// <remarks>A DryadLINQ user should not need to use this class directly.</remarks>
    public class VertexEnv
    {
        private const string VERTEX_EXCEPTION_FILENAME = @"VertexException.txt";

        private IntPtr m_nativeHandle;
        private UInt32 m_numberOfInputs;
        private UInt32 m_numberOfOutputs;
        private UInt32 m_nextInput;
        private UInt32 m_nextInputPort;
        private UInt32 m_nextOutputPort;
        private string[] m_argList;
        private DryadLinqVertexParams m_vertexParams;
        private bool m_useLargeBuffer;

        /// <summary>
        /// Initializes an instnace of VertexEnv. This is called in auto-generated vertex code.
        /// </summary>
        /// <param name="args"></param>
        /// <param name="vertexParams"></param>
        public VertexEnv(string args, DryadLinqVertexParams vertexParams)
        {
            this.m_argList = args.Split('|');
            this.m_nativeHandle = new IntPtr(Int64.Parse(this.m_argList[0], NumberStyles.HexNumber));
            this.m_numberOfInputs = DryadLinqNative.GetNumOfInputs(this.m_nativeHandle);
            this.m_numberOfOutputs = DryadLinqNative.GetNumOfOutputs(this.m_nativeHandle);
            this.m_nextInput = 0;
            this.m_nextInputPort = 0;
            this.m_nextOutputPort = 0;
            this.m_vertexParams = vertexParams;
            this.m_useLargeBuffer = vertexParams.UseLargeBuffer;
            if (this.m_numberOfOutputs > 0)
            {
                this.SetInitialWriteSizeHint();
            }

            // Set the thread count for DryadLINQ vertex runtime
            string threadCountStr = Environment.GetEnvironmentVariable("DRYAD_THREADS_PER_WORKER");
            DryadLinqVertex.ThreadCount = Environment.ProcessorCount;
            if (!String.IsNullOrEmpty(threadCountStr))
            {
                if (!Int32.TryParse(threadCountStr, out DryadLinqVertex.ThreadCount))
                {
                    throw new DryadLinqException("The env variable DRYAD_THREADS_PER_WORKER was set to " + threadCountStr);
                }
                if (DryadLinqVertex.ThreadCount < 1)
                {
                    DryadLinqVertex.ThreadCount = Environment.ProcessorCount;
                }
            }
        }

        internal IntPtr NativeHandle
        {
            get { return this.m_nativeHandle; }
        }

        /// <summary>
        /// The number of inputs of the vertex.
        /// </summary>
        public UInt32 NumberOfInputs
        {
            get { return this.m_numberOfInputs; }
        }        

        /// <summary>
        /// The number of outputs of the vertex.
        /// </summary>
        public UInt32 NumberOfOutputs
        {
            get { return this.m_numberOfOutputs; }
        }

        /// <summary>
        /// The number of command-line arguments of the vertex. 
        /// </summary>
        public Int32 NumberOfArguments
        {
            get { return this.m_argList.Length; }
        }
        
        /// <summary>
        /// Gets the argument at the specified index.
        /// </summary>
        /// <param name="idx"></param>
        /// <returns></returns>
        public string GetArgument(Int32 idx)
        {
            return this.m_argList[idx];
        }

        private bool UseLargeBuffer
        {
            get { return this.m_useLargeBuffer; }
        }

        /// <summary>
        /// Gets the vertex id.
        /// </summary>
        public Int64 VertexId
        {
            get {
                return DryadLinqNative.GetVertexId(this.m_nativeHandle);
            }
        }
        
        /// <summary>
        /// Makes a reader for the current input.
        /// </summary>
        /// <typeparam name="T">The record type of the input.</typeparam>
        /// <param name="readerFactory">The reader factory.</param>
        /// <returns>A reader for the current input.</returns>
        public DryadLinqVertexReader<T> MakeReader<T>(DryadLinqFactory<T> readerFactory)
        {
            bool keepPortOrder = this.m_vertexParams.KeepInputPortOrder(this.m_nextInput);
            UInt32 startPort = this.m_nextInputPort;
            this.m_nextInputPort += this.m_vertexParams.InputPortCount(this.m_nextInput);
            UInt32 endPort = this.m_nextInputPort;
            this.m_nextInput++;
            return new DryadLinqVertexReader<T>(this, readerFactory, startPort, endPort, keepPortOrder);
        }

        /// <summary>
        /// Make a writer for the current output.
        /// </summary>
        /// <typeparam name="T">The record type of the output.</typeparam>
        /// <param name="writerFactory">The writer factory.</param>
        /// <returns>A writer for the current output.</returns>
        public DryadLinqVertexWriter<T> MakeWriter<T>(DryadLinqFactory<T> writerFactory)
        {
            if (this.m_nextOutputPort + 1 < this.m_vertexParams.OutputArity)
            {
                UInt32 portNum = this.m_nextOutputPort++;
                return new DryadLinqVertexWriter<T>(this, writerFactory, portNum);
            }
            else
            {
                UInt32 startPort = this.m_nextOutputPort;
                UInt32 endPort = this.NumberOfOutputs;
                return new DryadLinqVertexWriter<T>(this, writerFactory, startPort, endPort);                
            }
        }

        /// <summary>
        /// Make a binary reader from a native stream.  Used only by auto-generated code.
        /// </summary>
        /// <param name="nativeStream">A native stream</param>
        /// <returns>A binary reader</returns>
        public static DryadLinqBinaryReader MakeBinaryReader(NativeBlockStream nativeStream)
        {
            return new DryadLinqBinaryReader(nativeStream);
        }

        /// <summary>
        /// Make a binary reader from a native handle and a port number. Used only by auto-generated code.
        /// </summary>
        /// <param name="handle">The native handle</param>
        /// <param name="port">The port number</param>
        /// <returns>A binary reader</returns>
        public static DryadLinqBinaryReader MakeBinaryReader(IntPtr handle, UInt32 port)
        {
            return new DryadLinqBinaryReader(handle, port);
        }

        /// <summary>
        /// Make a binary writer from a native stream. Used only by auto-generated code.
        /// </summary>
        /// <param name="nativeStream">A native stream</param>
        /// <returns>A binary writer</returns>
        public static DryadLinqBinaryWriter MakeBinaryWriter(NativeBlockStream nativeStream)
        {
            return new DryadLinqBinaryWriter(nativeStream);
        }

        /// <summary>
        /// Make a binary writer from a native handle and a port number. Used only by auto-generated code.
        /// </summary>
        /// <param name="handle">The native handle</param>
        /// <param name="port">The port number</param>
        /// <param name="buffSize">A hint of the size of write buffer</param>
        /// <returns>A binary writer</returns>
        public static DryadLinqBinaryWriter MakeBinaryWriter(IntPtr handle, UInt32 port, Int32 buffSize)
        {
            return new DryadLinqBinaryWriter(handle, port, buffSize);
        }

        private static Exception s_lastReportedException;
        internal static int ErrorCode { get; set; }

        /// <summary>
        /// This method is called by the generated vertex code, as well as VertexBridge
        /// to report exceptions. The exception will be dumped to "VertexException.txt"
        /// in the working directory.
        /// </summary>
        /// <param name="e">The exception that triggers to call this method.</param>
        public static void ReportVertexError(Exception e)
        {
            // We first need to check whether the same exception object was already
            // reported recently, and ignore the second call.
            //
            // This will be the case for most vertex exceptions because 1) the generated
            // vertex code catches the exceptions, calls ReportVertexError and rethrows,
            // and right after that 2) VertexBridge will receive the same exception
            // wrapped in a TargetInvocationException, and call ReportVertexError again
            // after extracting the inner exception.
            //
            // The second call from the VertexBridge is necessary because some exceptions
            // (particularly TypeLoadException due to static ctors) happen in the vertex DLL,
            // but just before the try/catch blocks in the vertex entry point (therefore
            // are missed by 1).
            if (s_lastReportedException == e) return;
                        
            s_lastReportedException = e;
                        
            // add to DryadLinqLog
            DryadLinqLog.AddInfo("Vertex failed with the following exception:");
            DryadLinqLog.AddInfo("{0}", e.ToString());

            // also write out to the standalone vertex exception file in the working directory
            using (StreamWriter exceptionFile = new StreamWriter(VERTEX_EXCEPTION_FILENAME))
            {
                exceptionFile.WriteLine(e.ToString());
            }
            if (ErrorCode == 0) throw e;
        }

        internal unsafe Int32 GetWriteBuffSize()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (UInt32)sizeof(MEMORYSTATUSEX);
            UInt64 maxSize = 512 * 1024 * 1024UL;
            if (DryadLinqNative.GlobalMemoryStatusEx(ref memStatus))
            {
                maxSize = memStatus.ullAvailPhys / 4;
            }
            if (this.m_vertexParams.RemoteArch == "i386")
            {
                maxSize = Math.Min(maxSize, 1024 * 1024 * 1024UL);
            }
            if (this.NumberOfOutputs > 0)
            {
                maxSize = maxSize / this.NumberOfOutputs;
            }

            UInt64 buffSize = (this.UseLargeBuffer) ? (256 * 1024 * 1024UL) : (8 * 1024 * 1024UL);
            if (buffSize > maxSize) buffSize = maxSize;
            if (buffSize < (16 * 1024UL)) buffSize = 16 * 1024;
            return (Int32)buffSize;
        }

        internal Int64 GetInputSize()
        {
            Int64 totalSize = 0;
            for (UInt32 i = 0; i < this.m_numberOfInputs; i++)
            {
                Int64 channelSize = DryadLinqNative.GetExpectedLength(this.NativeHandle, i);
                if (channelSize == -1) return -1;
                totalSize += channelSize;
            }
            return totalSize;
        }

        internal void SetInitialWriteSizeHint()
        {
            Int64 inputSize = this.GetInputSize();
            UInt64 hsize = (inputSize == -1) ? (5 * 1024 * 1024 * 1024UL) : (UInt64)inputSize;
            hsize /= this.NumberOfOutputs;
            for (UInt32 i = 0; i < this.NumberOfOutputs; i++)
            {
                DryadLinqNative.SetInitialSizeHint(this.m_nativeHandle, i, hsize);
            }
        }

        // The Vertex Host native layer will use this bridge method to invoke the vertex
        // entry point instead of invoking it directly through the CLR host.
        // This has the advantage of doing all the assembly load and invoke work for the
        // generated vertex assembly to happen in a managed context, so that any type or
        // assembly load exceptions can be caught and reported in full detail.
        private static void VertexBridge(string logFileName, string vertexBridgeArgs)
        {
            DryadLinqLog.Initialize(Constants.LoggingInfoLevel, logFileName);
            DryadLinqLog.AddInfo(".NET runtime version = v{0}.{1}.{2}",
                                 Environment.Version.Major,
                                 Environment.Version.Minor,
                                 Environment.Version.Build);
            DryadLinqLog.AddInfo(".NET runtime GC = {0}({1})",
                                 (GCSettings.IsServerGC) ? "ServerGC" : "WorkstationGC",
                                 GCSettings.LatencyMode);

            try
            {
                string[] splitArgs = vertexBridgeArgs.Split(',');
                if (splitArgs.Length != 4)
                {
                    throw new ArgumentException(string.Format(SR.VertexBridgeBadArgs, vertexBridgeArgs),
                                                "vertexBridgeArgs");
                }

                // We assume that the vertex DLL is in the job dir (currently always one level up from the WD).
                string moduleName = Path.Combine("..", splitArgs[0]);
                string className = splitArgs[1];
                string methodName = splitArgs[2];
                string nativeChannelString = splitArgs[3];

                Assembly vertexAssembly = Assembly.LoadFrom(moduleName);
                DryadLinqLog.AddInfo("Vertex Bridge loaded assembly {0}", vertexAssembly.Location);

                MethodInfo vertexMethod = vertexAssembly.GetType(className)
                                                        .GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
                vertexMethod.Invoke(null, new object[] { nativeChannelString });
            }
            catch (Exception e)
            {
                // Any exception that happens in the vertex code will come wrapped in a
                // TargetInvocationException since we're using Invoke(). We only want to
                // report the inner exception in this case. If the exception is of another
                // type (most likely one coming from the Assembly.LoadFrom() call), then
                // we will report it as is.
                if (e is TargetInvocationException && e.InnerException != null)
                {
                    ReportVertexError(e.InnerException);
                    if (ErrorCode == 0) throw e.InnerException;
                }
                else
                {
                    ReportVertexError(e);
                    if (ErrorCode == 0) throw;
                }
            }
        }

    }
}
