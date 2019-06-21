# SimpleSignals
A minimal event management system that uses custom attributes to automatically register event handlers

## Features
- **Manage Signal listener registration at the component level.

    - SimpleSignals automatically creates listener delegates for any component you register with the SignalManager. This way you don't have to write code to bind each listener individually.

    - SimpleSignals automatically removes listener delegates when it detects the target has been destroyed, so you don't have to write that code either.

- **SimpleSignals uses delegats to bind Signals to their listeners.

    - Unity's built in `SendMessage()` and `BroadcastMessage()` are intended for rapid prototyping and are designed to be flexible rather than performant. Unity reccomends using C#'s native delegates/Events in production code.

    - Type information about listener classes is cached so registering subsequent instances of the same class/component uses an efficent look up.

- **Signals are strongly typed and define their required parameters (including nullable types).
- **Supports multiple `SignalContexts` so you can constrain what signals are available in what contexts (if your application has become complex enough to require it).

## Usage
Create an instance of a SignalManager that you want to register Signals and listners with. 

```
this.signalManager = this.AddComponent<SignalManager>();
```

Next, define some Signals that you want to be able to `Invoke()` and that components can listen to by providing a listener. Becase Signals are strongly typed and they define the types of their parameters it makes it easy for programmers to know what parameters they need to impelment in their Signal Listeners.

```
namespace MyGameNamespace 
{

    // Input Signals
    public class TapInputSignal : Signal{}
    public class SwipeInputSignal : Signal<Vector2,float>{} // SwipeVector, SwipeTime
}
```

I like to put all my Signal definitions in a single file. This provides an easy start point for someone new to the code to understand what's going on in the application. All they need to do is use the *Find References...* feature of their code editor and they can see all the places a Signal is invoked from or listened to.

To invoke a signal and notify its listeners you call `Invoke<SignalType>(...)`, in this example when the player lifts their finger we only want to register a Swipe if they've moved a minimum threshold. This could be an important distinction if your game performs one action on a tap and another on a swipe.

*Note: In this example the `GlobalContext` class is Singleton that holds a reference to the `SingleManager` instance we want to use for signal binding. In your app, you can use any way of getting a reference to your `SignalManager` that makes sense in your codebase.*

```
public void OnPointerUp(Vector2 position)
{
    if (this.timeSwipeBegan.HasValue)
    {
        float swipeTime = Time.unscaledTime - this.timeSwipeBegan.Value;
        Vector2 swipeVector = position - this.pointerDownPosition;

        if (swipeVector.magnitude > swipeThreshold)
        {
            GlobalContext.SignalManager.Invoke<SwipeInputSignal>(swipeVector, swipeTime);
        }
        else
        {
            GlobalContext.SignalManager.Invoke<TapInputSignal>();
        }

        this.timeSwipeBegan = null;
    }
}
```

Now you can write a component that listens to any of these signals all you need to do is create a method with matching parameters and decorate it with a `[ListenTo]` attribute and register the component with the SignalManager using the `BindSignals()`

```
public class MyInputHandler : MonoBehavior
{
    void Start()
    {
        SignalManager.BindSignals(GlobalContext.SignalManager, this)
    }

    [ListenTo(typeof(SwipeInputSignal))]
    public void OnSwipeInputSignal(Vector2 swipe, float time)
    {
        Debug.Log("Swipe Vector:" + swipe + " magnitude:" + swipe.magnitude + " time:" + time + ");
    }

    [ListenTo(typeof(TapInputSignal))]
    public void OnTapInputSignal()
    {
        Debug.Log("Tapped!");
    }
}
```

Thats it, SimpleSignals will handle all the binding and unbinding of all your listeners and automatically clean them up when the object is destroyed.
