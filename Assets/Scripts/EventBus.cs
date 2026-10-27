using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;


/// <summary>
/// using Base in the EventBus Dictionary
/// </summary>
public abstract class EventInfoBase { }

public class EventInfo<T> : EventInfoBase
{
    public UnityAction<T> actions;
}

public class EventInfo : EventInfoBase
{
    public UnityAction actions;
}



public class EventBus : BaseManager<EventBus>
{
    public Dictionary<string, EventInfoBase> eventDictionary = new Dictionary<string, EventInfoBase>();

    private EventBus(){ }


    #region Eventbus functions
    /// <summary>
    /// Event Trigger
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="eventName"></param>
    /// <param name="info"></param>
    public void EventTrigger<T>(string eventName, T info)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            (eventDictionary[eventName] as EventInfo<T>)?.actions.Invoke(info);
        }
    }

    public void EventTrigger(string eventName)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            (eventDictionary[eventName] as EventInfo)?.actions.Invoke();
        }
    }


    /// <summary>
    /// Add EventListener
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="eventName"></param>
    /// <param name="func"></param>
    public void AddEventListener<T>(string eventName, UnityAction<T> func)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            (eventDictionary[eventName] as EventInfo<T>).actions += func;
        }
        else
        {
            eventDictionary.Add(eventName, new EventInfo<T>());
            (eventDictionary[eventName] as EventInfo<T>).actions += func;
        }
    }

    public void AddEventListener(string eventName, UnityAction func)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            (eventDictionary[eventName] as EventInfo).actions += func;
        }
        else
        {
            eventDictionary.Add(eventName, new EventInfo());
            (eventDictionary[eventName] as EventInfo).actions += func;
        }
    }


    /// <summary>
    /// Remove EventListener
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="eventName"></param>
    /// <param name="func"></param>
    public void RemoveEventListener<T>(string eventName, UnityAction<T> func)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            (eventDictionary[eventName] as EventInfo<T>).actions -= func;
        }
    }

    public void RemoveEventListener(string eventName, UnityAction func)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            (eventDictionary[eventName] as EventInfo).actions -= func;
        }
    }


    /// <summary>
    /// Clear Event or whole EventBus
    /// </summary>
    public void ClearEventBus()
    {
        eventDictionary.Clear();
    }

    public void ClearEvent(string eventName)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            eventDictionary.Remove(eventName);
        }
    }

    #endregion

}


