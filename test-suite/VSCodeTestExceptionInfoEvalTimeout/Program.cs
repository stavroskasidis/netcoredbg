using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using NetcoreDbgTest;
using NetcoreDbgTest.VSCode;
using NetcoreDbgTest.Script;

using Newtonsoft.Json;

namespace NetcoreDbgTest.Script
{
    class Context
    {
        public void PrepareStart(string caller_trace)
        {
            InitializeRequest initializeRequest = new InitializeRequest();
            initializeRequest.arguments.clientID = "vscode";
            initializeRequest.arguments.clientName = "Visual Studio Code";
            initializeRequest.arguments.adapterID = "coreclr";
            initializeRequest.arguments.pathFormat = "path";
            initializeRequest.arguments.linesStartAt1 = true;
            initializeRequest.arguments.columnsStartAt1 = true;
            initializeRequest.arguments.supportsVariableType = true;
            initializeRequest.arguments.supportsVariablePaging = true;
            initializeRequest.arguments.supportsRunInTerminalRequest = true;
            initializeRequest.arguments.locale = "en-us";
            Assert.True(VSCodeDebugger.Request(initializeRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            LaunchRequest launchRequest = new LaunchRequest();
            launchRequest.arguments.name = ".NET Core Launch (console) with pipeline";
            launchRequest.arguments.type = "coreclr";
            launchRequest.arguments.preLaunchTask = "build";
            launchRequest.arguments.program = ControlInfo.TargetAssemblyPath;
            launchRequest.arguments.cwd = "";
            launchRequest.arguments.console = "internalConsole";
            launchRequest.arguments.stopAtEntry = true;
            launchRequest.arguments.internalConsoleOptions = "openOnSessionStart";
            launchRequest.arguments.__sessionId = Guid.NewGuid().ToString();
            Assert.True(VSCodeDebugger.Request(launchRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void PrepareEnd(string caller_trace)
        {
            ConfigurationDoneRequest configurationDoneRequest = new ConfigurationDoneRequest();
            Assert.True(VSCodeDebugger.Request(configurationDoneRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasEntryPointHit(string caller_trace)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "entry")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasExit(string caller_trace)
        {
            bool wasExited = false;
            int ?exitCode = null;
            bool wasTerminated = false;

            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "exited")) {
                    wasExited = true;
                    ExitedEvent exitedEvent = JsonConvert.DeserializeObject<ExitedEvent>(resJSON);
                    exitCode = exitedEvent.body.exitCode;
                }
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "terminated")) {
                    wasTerminated = true;
                }
                if (wasExited && exitCode == 0 && wasTerminated)
                    return true;

                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void DebuggerExit(string caller_trace)
        {
            DisconnectRequest disconnectRequest = new DisconnectRequest();
            disconnectRequest.arguments = new DisconnectArguments();
            disconnectRequest.arguments.restart = false;
            Assert.True(VSCodeDebugger.Request(disconnectRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void SetExceptionBreakpointsAll(string caller_trace)
        {
            SetExceptionBreakpointsRequest setExceptionBreakpointsRequest = new SetExceptionBreakpointsRequest();
            setExceptionBreakpointsRequest.arguments.filters.Add("all");
            Assert.True(VSCodeDebugger.Request(setExceptionBreakpointsRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasExceptionHit(string caller_trace)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "exception")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public ExceptionDetails GetExceptionDetails(string caller_trace)
        {
            ExceptionInfoRequest exceptionInfoRequest = new ExceptionInfoRequest();
            exceptionInfoRequest.arguments.threadId = threadId;
            var ret = VSCodeDebugger.Request(exceptionInfoRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            ExceptionInfoResponse exceptionInfoResponse =
                JsonConvert.DeserializeObject<ExceptionInfoResponse>(ret.ResponseStr);
            return exceptionInfoResponse.body.details;
        }

        public void Continue(string caller_trace)
        {
            ContinueRequest continueRequest = new ContinueRequest();
            continueRequest.arguments.threadId = threadId;
            Assert.True(VSCodeDebugger.Request(continueRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public Context(ControlInfo controlInfo, NetcoreDbgTestCore.DebuggerClient debuggerClient)
        {
            ControlInfo = controlInfo;
            VSCodeDebugger = new VSCodeDebugger(debuggerClient);
        }

        ControlInfo ControlInfo;
        VSCodeDebugger VSCodeDebugger;
        int threadId = -1;
    }
}

namespace VSCodeTestExceptionInfoEvalTimeout
{
    class SlowException : Exception
    {
        public SlowException(string message, Exception inner) : base(message, inner) { }

        // Blocks in native code (a read of the debuggee's stdin, which nobody writes), where neither the eval timeout nor
        // the abort after it can end it: until it ends, the runtime refuses any other eval on the thread
        // (CORDBG_E_FUNC_EVAL_BAD_START_POINT).
        public override string StackTrace { get { Console.ReadLine(); return base.StackTrace; } }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Label.Checkpoint("init", "exception_info_test", (Object context) => {
                Context Context = (Context)context;
                Context.PrepareStart(@"__FILE__:__LINE__");
                Context.SetExceptionBreakpointsAll(@"__FILE__:__LINE__");
                Context.PrepareEnd(@"__FILE__:__LINE__");
                Context.WasEntryPointHit(@"__FILE__:__LINE__");
                Context.Continue(@"__FILE__:__LINE__");
            });

            try
            {
                throw new SlowException("the outer one", new ArgumentException("the inner one"));
            }
            catch (SlowException e)
            {
                Console.WriteLine(e.Message);
            }

            Label.Checkpoint("exception_info_test", "finish", (Object context) => {
                Context Context = (Context)context;
                Context.WasExceptionHit(@"__FILE__:__LINE__");

                // The stack trace's getter can't be aborted, and the evals after it fail: the message and the inner
                // exception, read from their fields, are still there.
                ExceptionDetails details = Context.GetExceptionDetails(@"__FILE__:__LINE__");
                Assert.True(details.fullTypeName == "VSCodeTestExceptionInfoEvalTimeout.SlowException", @"__FILE__:__LINE__");
                Assert.True(details.message == "the outer one", @"__FILE__:__LINE__");
                Assert.True(details.innerException != null && details.innerException.Count == 1, @"__FILE__:__LINE__");
                Assert.True(details.innerException[0].fullTypeName == "System.ArgumentException", @"__FILE__:__LINE__");
                Assert.True(details.innerException[0].message == "the inner one", @"__FILE__:__LINE__");
            });

            // The program can't go on: its thread is still in the blocked getter. Disconnecting ends it.
            Label.Checkpoint("finish", "", (Object context) => {
                Context Context = (Context)context;
                Context.DebuggerExit(@"__FILE__:__LINE__");
            });
        }
    }
}
