namespace com.demo
{
    
    /// <ExplanationForDemo>
    ///     The interfaces used by EventBus
    /// <!ExplanationForDemo>
    
    public interface IEventBusEventDemo
    {
    }
    public interface IEventBusListenerDemo
    {
    }
    public interface IEventBusListenerDemo<T> : IEventBusListenerDemo where T : IEventBusEventDemo
    { 
        public bool OnBusEventTriggered(T data);
    }
}