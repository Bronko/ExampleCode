using System;
using System.Collections.Generic;
using System.Linq;
using com.pnkfrg.log;
using UnityEngine;

namespace com.demo
{
    /// <ExplanationForDemo>
    ///     A light weight global event bus.
    ///     This helps detangling initialization order dependencies.
    ///     The team needs to be strict in controlling usage of this.
    ///     It was only to be used to remove dependencies between high level concepts.
    ///
    ///     Features:
    ///         - Fire an "event" anywhere in a one liner. No need to boiler plate anything.
    ///         - Receive events by implementing IEventBusListener interface.
    ///             - The requirement to implement the interface raises attention in code review process
    ///             - The requirement to specify the type in the interface implementation keeps tracking code pathes and dependencies easy
    ///         - Event reaction order: Last subscriber in, first out.
    ///             - Events can be "consumed", meaning following subscribers will not receive it, if specified.
    ///
    ///      Potential alternative:
    ///         - Pass event handling func in Register call, and instead of listeners, keep lists of funcs (Return value for consuming feature).
    ///         
    /// <!ExplanationForDemo>

    /// <summary>
    /// Here were links to the documentation
    /// </summary>
    public static class EventBusDemo 
    {
        private static readonly Dictionary<Type, List<IEventBusListener>> AllListeners; 
        
        static EventBusDemo()
        {
            AllListeners = new Dictionary<Type, List<IEventBusListener>>();
        }
        public static void Dispose()
        {
            AllListeners.Clear();
        }

        public static void Fire<T>(T data) where T : IEventBusEvent
        {
            if (AllListeners.TryGetValue(data.GetType(), out var listeners))
            {
                var consumed = false;
                var copy = listeners.ToList();
                for (int i = copy.Count -1; i >= 0 && !consumed; --i)
                {
                    consumed = ((IEventBusListener<T>) copy[i]).OnBusEventTriggered(data);
                }
            }
        }
        /// <summary>
        /// Subscribe the listener to the EventBus for the given IBusEvent (data) T.
        /// </summary>
        public static void Subscribe<T>(IEventBusListener<T> listener) where T : IEventBusEvent
        {
            var dataType = typeof(T);
            
            if (!AllListeners.ContainsKey(dataType))
            {
                AllListeners.Add(dataType, new List<IEventBusListener>());
            }
            var specificListeners = AllListeners[dataType];
            if (specificListeners.Contains(listener))
                Log.Error(nameof(EventBus),"Listeners cannot be added twice!"); //Log Error causes error handling flow
            
            specificListeners.Add(listener);
        }

        /// <summary>
        /// Unsubscribe the listener to the EventBus for the given IBusEvent (data) T.
        /// </summary>
        public static void Unsubscribe<T>(IEventBusListener<T> listener) where T : IEventBusEvent
        {
            var dataType = typeof(T);
            
            if (!AllListeners.ContainsKey(dataType))
            {
                Debug.LogWarning("Attempt to unsubscribe without being subscribed before");
                return;
            }
            var specificListeners = AllListeners[dataType];

            if (!specificListeners.Remove(listener))
            {
                Debug.LogWarning("Attempt to unsubscribe without being subscribed before");
            }
        }
    }
}