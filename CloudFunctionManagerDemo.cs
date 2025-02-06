using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace com.demo
{
    
    /// <ExplanationForDemo>
    ///     Example of solving a complex promblem, and providing an easy api. 
    /// 
    ///     Reacting to Internet connection issues for server communication is complex by design.
    ///     This Manager provides an API, to take care of all edge cases, and make implementing calls easy and light weight.
    ///     The actual server call is handled in the "CloudFunctionService", making that solution exchangeable and independent.
    /// 
    ///     A flow chart was created for this in the company Miro.
    ///     In a perfect world, this would have been test covered.
    ///
    ///     Features:
    ///         - Handles infinite amount of calls, while priors are still active
    ///         - Reacts to connectivity issues, and reattempts calls, after resolving
    ///         - Optionally shows loading spinner
    ///              - Define loading spinner behavior: Instantly, never, or after time out defined in server configuration.
    ///         - Fire AndForget calls (The client will not await them, and can simulate results for a smoother user experience)
    ///         - Reacts to "basePayload" in server answers, like updates to user data or resources, to minimize server communication.
    ///         - Calls flagged as transaction will be queued up, to work as a transaction
    /// <!ExplanationForDemo>
    
    
    
    /// <summary>
    ///     API for server calls.
    ///     Main Call method engages in a timeOut Flow, will cancel calls whe connectivity issues occur,
    ///     and reattempt them after the connectivity issue is resolved.
    ///
    ///     Here were also links to the documentation
    /// </summary>
    public class CloudFunctionManagerDemo : ManagerBase, IEventBusListener<ConnectionStateChangedEvent>
    {
        //Meaning of order: Lower entries can override higher entries for each server call. 
        //Used, e.g. Call1 didn't require a spinner to be ever shown, but Call2 wants it after the timeOut.
        public enum SpinnerMode
        {
            Invisible,
            AfterTimeOut,
            Instant
        }

        private enum TimeOutPhase
        {
            ToSpinner,
            ToPopup
        }

        private enum State
        {
            Idle,
            Processing,
            TimedOut,
            Error
        }


        private readonly List<CancellationTokenSource>
            cancelSources = new(); // In case of time out, we cancel all active calls, using these cancel token sources

        private readonly Dictionary<int, Action>
            wrappedServerCalls = new(); // The int key allows calls to remove themselves after successfully finishing. 

        private readonly Dictionary<Type, bool>
            transactionHoldFlags = new(); // For the additional transaction feature.

        private SpinnerMode currentSpinnerMode = SpinnerMode.Invisible;
        private int nextWrappedServerCallId;
        private State state;
        private CancellationTokenSource timeOutFlowCancellationSource;

        //Fields for "TimeOutOrFinish" to read how long to wait, and if the timeOut is supposed to be increased.
        private float timeOutToSpinner;
        private float timeOutToPopup;
        
        protected override Task InternalInit()
        {
            nextWrappedServerCallId = 0;
            EventBus.Subscribe(this);

            return Task.CompletedTask;
        }
        
        
        /// <summary>
        ///     Calling the server, wrapped in a time out flow, which force triggers a connectivity issue flow after timing out.
        ///     When a connectivity issue is triggered by this or from outside, calls are aborted.
        ///     After concluding connectivity issue, the calls will be reattempted.
        /// </summary>
        /// <param name="parameters">server parameters</param>
        /// <param name="loadingSpinnerMode">
        ///     Show the loading spinner instantly, after the spinner time out, or keep it invisible, if no
        ///     other ongoing call wants to show the spinner
        /// </param>
        /// <typeparam name="T">The server call type, which holds the fields for parsing the Json result</typeparam>
        /// <returns></returns>
        public async Task<T> Call<T>(Dictionary<string, object> parameters,
            SpinnerMode loadingSpinnerMode = SpinnerMode.AfterTimeOut)
            where T : FunctionBase, new()
        {
            Log.Msg(this, $"Calling cloud function{typeof(T).Name} with spinner mode {loadingSpinnerMode}!",
                LogChannel.CloudFunctions);

            var doTransact = typeof(ITransactionFunction).IsAssignableFrom(typeof(T));
            if (doTransact)
                await HandleTransactingCall<T>();

            UpdateSpinnerMode(loadingSpinnerMode);

            var tsc = new TaskCompletionSource<T>();

            var wrappedServerCallId = nextWrappedServerCallId;
            nextWrappedServerCallId++;
            var startingTime = DateTime.UtcNow;

            await CreateAndHandleServerTask(parameters, tsc, wrappedServerCallId);

            ReactToBasePayload(startingTime, parameters, tsc.Task.Result);

            if (doTransact)
                transactionHoldFlags[typeof(T)] = false;

            return tsc.Task.Result;
        }
        
        /// <summary>
        ///     Calling the server with parameters, and not awaiting any results. 
        /// </summary>
        /// <param name="parameters">server parameters</param>
        /// <typeparam name="T">The Server Call Type</typeparam>
        /// <returns></returns>
        public async void CallAndForget<T>(Dictionary<string, object> parameters = null)
            where T : FunctionBase, new()
        {
            await CallAndIgnoreIssues<T>(parameters);
        }

        /// <summary>
        /// Calling the server with parameters. There is no client side time out handling, and no spinner. 
        /// Server sided time out and errors will result in returning null.
        /// </summary>
        /// <param name="parameters">server parameters</param>
        /// <typeparam name="T">The server call type, which holds the fields for parsing the Json result</typeparam>
        /// <returns></returns>
        public async Task<T> CallAndIgnoreIssues<T>(Dictionary<string, object> parameters = null)
            where T : FunctionBase, new()
        {
            Log.Msg(this, $"Calling cloud function{typeof(T).Name} without treating issues!", LogChannel.CloudFunctions);
            Assert.IsFalse(
                typeof(ITransactionFunction).IsAssignableFrom(typeof(T)), "Transactions cannot ignore issues");
            
            var cts = new CancellationTokenSource();

            var queryTask = ServiceLocator.Get<BackendFunctionService>().Call<T>(cts, parameters);

            T result = null;
            var startingTime = DateTime.UtcNow;

            await queryTask.WaitOrCancel(cts.Token).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully) //Task completed
                {
                    result = task.Result;
                }
                else if (task.IsFaulted)
                {
                    Log.SilentError(this, task.Exception); // //Logging a "silent error" will not initiate the error flow, but log it on the server
                }
                else  //Cancelled. The task will be only cancelled, if the BackendFunctionService determined connectivity issues 
                {
                    if (!timeOutFlowCancellationSource.IsCancellationRequested)
                        timeOutFlowCancellationSource.Cancel();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
            ReactToBasePayload(startingTime, parameters, result);
            return result;
        }
        

        /// <summary>
        /// Wrapping the task in order to be callable again, if it was aborted due to connectivity issues.
        /// Execute it, if there are no current connectivity issues.
        /// </summary>
        private async Task CreateAndHandleServerTask<T>(Dictionary<string, object> parameters, TaskCompletionSource<T> tsc,
            int wrappedServerCallId) where T : FunctionBase, new()
        {
            void WrappedTask()
            {
                ExecuteServerCall(parameters, tsc, wrappedServerCallId);
            }

            wrappedServerCalls.Add(wrappedServerCallId, WrappedTask);
            
            if (state != State.TimedOut)
            {
                WrappedTask();
                TimeOutFlow();
            }

            await tsc.Task;
        }

        private void UpdateSpinnerMode(SpinnerMode spinnerMode)
        {
            if (spinnerMode > currentSpinnerMode)
                currentSpinnerMode = spinnerMode;

            if (currentSpinnerMode == SpinnerMode.Instant)
                ShowSpinner(true);
        }

        private async Task HandleTransactingCall<T>() where T : FunctionBase, new()
        {
            if (!transactionHoldFlags.ContainsKey(typeof(T)))
                transactionHoldFlags.Add(typeof(T), false);

            while (transactionHoldFlags[typeof(T)])
                await Awaiters.NextFrame;

            transactionHoldFlags[typeof(T)] = true;
        }

        private void ShowSpinner(bool doShow)
        {
            if (doShow)
                Manager.Spinner.AddFullFrontClaim(this);
            else
                Manager.Spinner.RemoveFullFrontClaims(this);
        }

        /// <summary>
        ///     During each time out, the flow will be left, if the state had been reset to idle.
        ///     TimeOutOrFinish() will set the state to idle, if it finds no more active task in the wrappedServerCalls.
        /// </summary>
        private async void TimeOutFlow()
        {
            //Setting the timeOutFields to server config value. A running time out will read from these variables to increment itself. A new time out will use these values.
            SetSelectedTimeOut(TimeOutPhase.ToSpinner);
            SetSelectedTimeOut(TimeOutPhase.ToPopup);

            //If the state is not idle, it means a time out flow is already active - hence we are leaving in that case.
            if (state != State.Idle)
            {
                return;
            }

            state = State.Processing;

            timeOutFlowCancellationSource = new CancellationTokenSource();

            await Awaiters
                .NextFrame; //Waiting one frame, so server calls placed in the same frame will not increment the time out each.

            await TimeOutOrFinish(TimeOutPhase.ToSpinner,
                timeOutFlowCancellationSource
                    .Token); //Waiting for the first time out, or all calls to be concluded
            if (state == State.Idle || state == State.Error)
            {
                return;
            }

            if (currentSpinnerMode == SpinnerMode.AfterTimeOut)
            {
                ShowSpinner(true);
            }

            await TimeOutOrFinish(TimeOutPhase.ToPopup,
                timeOutFlowCancellationSource
                    .Token); //Waiting for the first time out, or all calls to be concluded
            if (state == State.Idle || state == State.Error)
            {
                return;
            }

            state = State.TimedOut;
            ShowSpinner(false);
            AbortAllActiveCalls();

            await Manager.Connectivity.HandleConnectivityIssues(true); 

            InitiateReattempt();
        }
        
        private void AbortAllActiveCalls()
        {
            cancelSources.ForEach(cs => cs.Cancel());
            cancelSources.Clear();
        }

        private void InitiateReattempt()
        {
            state = State.Idle;

            foreach (var action in wrappedServerCalls.Values)
            {
                action();
            }

            currentSpinnerMode = SpinnerMode.Instant;
            ShowSpinner(true);

            TimeOutFlow();
        }

        /// <summary>
        ///     Caches the current set time out, and executes it.
        ///     Will abort and set the state to idle, if no more calls are in the wrappedServerCalls. (This means, all calls are completed!)
        ///     Will increment it's cached time out, if the selected time out had been increased due to a new server call.
        /// </summary>
        /// <param name="timeOutPhase">The time out phase, we are in.</param>
        private async Task TimeOutOrFinish(TimeOutPhase timeOutPhase, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return;

            var cachedTimeOutSeconds = timeOutPhase == TimeOutPhase.ToSpinner ? timeOutToSpinner : timeOutToPopup;
            
            SetSelectedTimeOut(timeOutPhase, 0); //Now the corresponding time out field is 0. An increment will only happen, when an additional flow caused the time out field to be set to server value again.

            while (cachedTimeOutSeconds > 0)
            {
                await Awaiters.NextFrame;
                var additionalTime = timeOutPhase == TimeOutPhase.ToSpinner ? timeOutToSpinner : timeOutToPopup;

                if (additionalTime > 0)
                {
                    cachedTimeOutSeconds += additionalTime;
                    SetSelectedTimeOut(timeOutPhase);
                }

                cachedTimeOutSeconds -= Time.deltaTime;

                if (ct.IsCancellationRequested)
                    return;

                if (!wrappedServerCalls.Any())
                {
                    //All calls have been concluded!
                    ResetManager();
                    return;
                }
            }
        }

        //Sets the corresponding time out field to the given value. Set to server config value if value not defined.
        private void SetSelectedTimeOut(TimeOutPhase timeOut, int? value = null)
        {
            if (timeOut == TimeOutPhase.ToSpinner)
            {
                timeOutToSpinner = value ?? Service.Config.Settings.FirstServerTimeOut;
            }
            else
            {
                timeOutToPopup = value ?? Service.Config.Settings.SecondServerTimeOut;
            }
        }

        private void ResetManager()
        {
            state = State.Idle;
            currentSpinnerMode = SpinnerMode.Invisible;
            SetSelectedTimeOut(TimeOutPhase.ToSpinner);
            SetSelectedTimeOut(TimeOutPhase.ToPopup);
            cancelSources.Clear();
            ShowSpinner(false);
        }

        /// <summary>
        ///     Wraps a server call in a task, that can be called again. Task will be added to a dictionary,
        ///     and a corresponding cancellation source will be added to a list.
        /// </summary>
        /// <param name="parameters">Server call paramerters</param>
        /// <param name="tsc">The Task Completion source, which will be awaited before the external caller returns the result</param>
        /// <param name="taskId">Key for the active tasks dictionary. If the task is complete, it will be removed from it.</param>
        /// <typeparam name="T">Server Call Type</typeparam>
        private async void ExecuteServerCall<T>(Dictionary<string, object> parameters, TaskCompletionSource<T> tsc,
            int taskId)
            where T : FunctionBase, new()
        {
            var cts = new CancellationTokenSource();
            var ct = cts.Token;
            cancelSources.Add(cts);
            
            var queryTask = ServiceLocator.Get<BackendFunctionService>().Call<T>(cts, parameters);

            await queryTask.WaitOrCancel(ct).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully) //Task completed
                {
                    cancelSources.Remove(cts);
                    tsc.SetResult(task.Result);
                    wrappedServerCalls.Remove(taskId);
                }
                else if (task.IsFaulted)
                {
                    ShowSpinner(false);
                    state = State.Error;
                    if (!timeOutFlowCancellationSource.IsCancellationRequested)
                        timeOutFlowCancellationSource.Cancel();
                    
                    Log.Error(this, task.Exception.InnerExceptions[0].Message); //Logging an error will initiate error flow
                }
                else //Cancelled. The task will be only cancelled, if the BackendFunctionService determined connectivity issues 
                {
                    if (!timeOutFlowCancellationSource.IsCancellationRequested)
                        timeOutFlowCancellationSource.Cancel();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }


        /// <summary>
        /// Hook to react to any additional information the server can add. This reduces traffic.
        /// </summary>
        /// <param name="startingTime"></param>
        /// <param name="parameters"></param>
        /// <param name="result"></param>
        private void ReactToBasePayload(DateTime startingTime, Dictionary<string, object> parameters,
            FunctionBase result)
        {
            if (result == null)
                return;
            
            if (!string.IsNullOrEmpty(result.UserData))
            {
                Manager.User.UpdateUserData(result);
            }

            if (!string.IsNullOrEmpty(result.Resources))
            {
                Manager.Resources.UpdateResources(result);
            }
        }

        /// <summary>
        /// Cancel the flow, when connectivity issues are detected
        /// </summary>
        /// <returns></returns>
        public bool OnBusEventTriggered(ConnectionStateChangedEvent data)
        {
            if (data.State is ConnectivityState.NoNothing or ConnectivityState.NoServer)
                timeOutFlowCancellationSource?.Cancel();
            return false;
        }
    }
    
}