using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace com.demo
{

    /// <ExplanationForDemo>
    ///    This is a wrapper for animations, duplicating the API of the Unity animator system, to combine thhe usage of the Unity
    ///    animator with own code driven solutions.
    ///    It is also providing a callback for animation flows that are considered "finished" or alternatively awaitable async wrappers.
    ///    The way to include own animation systems is to inherit from this class and override the "HandleXXX" methods.
    ///
    ///    This allows programmers to quickly set up an animation by code, and artists to override them by defining the parameters in the Unity animator.
    ///    It is also possible to daisy chain dynamic code driven animations (up to triggering particle systems) with static animator calls.
    ///
    ///    On the downside are some conventions for the definition of animation states when using the Unity animator.
    /// </explanationForDemo>

    /// <summary>
    /// Here were links to the documentation
    /// </summary>
    public class AnimationThingyDemo : MonoBehaviour
    {
    
    protected const string ShowTrigger = "Show";
    protected const string HideTrigger = "Hide";
    private const string ResetTrigger = "Reset";
    private const string ShowBoolean = "IsShowing";

    protected Animator animator;


    private HashSet<string> triggers = new();
    private Dictionary<string, bool> bools = new();
    private Dictionary<string, int> ints = new();
    private Dictionary<string, float> floats = new();

    private List<TaskCompletionSource<bool>> tcss = new();
    private List<Action> callbacks = new();
    private List<Tween> tweens = new();
    private int stackedAnimations = 0;

    protected virtual void Awake()
    {
        animator = gameObject.GetComponent<Animator>();
        if (animator != null)
            ReadAnimatorParameters();
    }

    private void ReadAnimatorParameters()
    {
        foreach (var param in animator.parameters)
        {
            switch (param.type)
            {
                case AnimatorControllerParameterType.Float:
                    floats.Add(param.name, param.defaultFloat);
                    break;
                case AnimatorControllerParameterType.Int:
                    ints.Add(param.name, param.defaultInt);
                    break;
                case AnimatorControllerParameterType.Bool:
                    bools.Add(param.name, param.defaultBool);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    triggers.Add(param.name);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    /// <summary>
    /// In case of using the Unity animator, this needs to be called from the animation flow itself, once it is considered finished
    /// </summary>
    [UsedImplicitly]
    protected virtual void OnAnimationFinished()
    {
        var doFire = stackedAnimations == 1;
        if (stackedAnimations > 0)
            stackedAnimations--;
        if (doFire)
        {
            var cache = callbacks.ToList();
            callbacks.Clear();
            cache.ForEach(c => c.Invoke());
        }
    }

    /// <summary>
    /// Easy API for setting default "Show" trigger and awaiting animation
    /// </summary>
    public async Task ShowAsync()
    {
        var tcs = AddNewTcs();
        Show(() => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    /// <summary>
    /// Easy API for setting default "Show" trigger
    /// </summary>
    public void Show(Action callback = null)
    {
        SetTrigger(ShowTrigger, callback);
    }

    /// <summary>
    /// Easy API for setting default "Hide" trigger
    /// </summary>
    public async Task ShowBoolAsync()
    {
        var tcs = AddNewTcs();
        ShowBool(() => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    /// <summary>
    /// Easy API for setting default "Hide" trigger
    /// </summary>
    public async Task HideBoolAsync()
    {
        var tcs = AddNewTcs();
        HideBool(() => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    private void SetTcsResult(TaskCompletionSource<bool> tcs, bool result)
    {
        if (!tcs.Task.IsCompleted)
            tcs.TrySetResult(result);
        tcss.Remove(tcs);
    }

    private TaskCompletionSource<bool> AddNewTcs()
    {
        var tcs = new TaskCompletionSource<bool>();
        tcss.Add(tcs);
        return tcs;
    }

    /// <summary>
    /// Easy API in case the state of being shown/hidden shall be preserved as boolean
    /// </summary>
    public void ShowBool(Action callback = null)
    {
        SetBool(ShowBoolean, true, callback);
    }

    /// <summary>
    /// Easy API in case the state of being shown/hidden shall be preserved as boolean
    /// </summary>
    public void HideBool(Action callback = null)
    {
        SetBool(ShowBoolean, false, callback);
    }


    /// <summary>
    /// All tweens created in own animations need to be added this way.
    /// That way, they already will be supported by callbacks or async wrappers.
    /// </summary>
    protected void AddTween(Tween tween, Action callback = null)
    {
        tweens.Add(tween);
        AddCallback(callback);
        if (callback != null)
            tween.onComplete += OnAnimationFinished;

        tween.onComplete += () => tweens.Remove(tween);
    }

    /// <summary>
    /// Removes any pending callback
    /// </summary>
    public void FlushCallbacks()
    {
        stackedAnimations = 0;
        callbacks.Clear();
    }

    private void KillTweens()
    {
        tweens.ForEach(t => t.Kill(false));
        tweens.Clear();
    }

    private void AddCallback(Action action)
    {
        if (action == null)
            return;

        stackedAnimations++;
        callbacks.Add(action);
    }

    public async Task SetFloatAsync(string name, float value)
    {
        var tcs = AddNewTcs();
        SetFloat(name, value, () => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    public void SetFloat(string name, float value, Action callback = null)
    {

        if (floats.ContainsKey(name))
        {
            floats[name] = value;
            AddCallback(callback);
            animator.SetFloat(name, value);
        }
        else
        {
            HandleFloat(name, value, callback);
        }
    }

    public async Task SetTriggerAsync(string name)
    {
        var tcs = AddNewTcs();
        SetTrigger(name, () => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    public void SetTrigger(string name, Action callback = null)
    {
        if (triggers.Contains(name))
        {
            AddCallback(callback);
            animator.SetTrigger(name);
        }
        else
        {
            HandleTrigger(name, callback);
        }
    }

    public async Task SetIntAsync(string name, int value)
    {
        var tcs = AddNewTcs();
        SetInt(name, value, () => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    public void SetInt(string name, int value, Action callback = null)
    {
        if (ints.ContainsKey(name))
        {
            ints[name] = value;
            AddCallback(callback);
            animator.SetInteger(name, value);
        }
        else
        {
            HandleInt(name, value, callback);
        }
    }

    public async Task SetBoolAsync(string name, bool value)
    {
        var tcs = AddNewTcs();
        SetBool(name, value, () => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    public void SetBool(string name, bool value, Action callback = null)
    {
        if (bools.ContainsKey(name))
        {
            bools[name] = value;
            AddCallback(callback);
            animator.SetBool(name, value);
        }
        else
        {
            HandleBool(name, value, callback);
        }
    }

    public virtual void Reset()
    {
        SetTrigger(ResetTrigger);
        FlushCallbacks();
        KillTweens();
        ClearTCSs();
    }

    private void ClearTCSs()
    {
        for (int i = tcss.Count - 1; i >= 0; --i)
        {
            SetTcsResult(tcss[i], false);
        }
    }

    public async Task HideAsync()
    {
        var tcs = AddNewTcs();
        Hide(() => SetTcsResult(tcs, true));
        await tcs.Task;
    }

    public void Hide(Action callback = null)
    {
        FlushCallbacks();
        KillTweens();
        SetTrigger(HideTrigger, callback);
    }

    protected virtual void OnDisable()
    {
        ClearTCSs();
    }

    /// <summary>
    /// Unity forgets the states when disabling the game objects. For this project, we wanted to remember.
    /// </summary>
    protected virtual void OnEnable()
    {
        foreach (var kvp in bools)
        {
            animator.SetBool(kvp.Key, kvp.Value);
        }

        foreach (var kvp in ints)
        {
            animator.SetInteger(kvp.Key, kvp.Value);
        }

        foreach (var kvp in floats)
        {
            animator.SetFloat(kvp.Key, kvp.Value);
        }

        Reset();
    }

    private void ShowParameterWarning(string methodname, string trigger)
    {
        if (trigger == ResetTrigger)
            return;
        Log.Warning(this,
            $"{methodname}: {trigger}: parameter neither exists in the animator, nor is there a code driven animation override.");
    }

    protected virtual void HandleBool(string name, bool value, Action callback)
    {
        ShowParameterWarning("bool", name);
    }

    protected virtual void HandleInt(string name, int value, Action callback)
    {
        ShowParameterWarning("int", name);
    }

    protected virtual void HandleFloat(string name, float value, Action callback)
    {
        ShowParameterWarning("float", name);
    }

    protected virtual void HandleTrigger(string name, Action callback)
    {
        ShowParameterWarning("trigger", name);
    }
    }
}

