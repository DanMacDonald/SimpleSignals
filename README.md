# SimpleSignals
A minimal event system that manages event listeners automatically (using custom attributes).

## Features
- **Register a component to bind all of its Singnal Listener methods**

    - SimpleSignals automatically creates delegate bindings for each listener method of a component you register with the SignalManager. This way you don't have to write code to bind each listener individually.

    - SimpleSignals automatically removes delegate bindings when it detects the target component has been destroyed, so you don't have to write that code either.

- **Efficent delegate based listener invocation**

    - Unity's built in `SendMessage()` and `BroadcastMessage()` are intended for rapid prototyping and are designed to be flexible rather than performant. Unity reccomends using C#'s native delegates/Events in production code.

    - SimpleSignals caches type information about registerd components so registering subsequent instances of the same class/component uses an efficent look up.

- **Simple attribute decoration to indicate listener methods**
    - Dramatically reduces the amount of boilerplate code that needs to be written for registering signal listeners.

- **Signals are strongly typed and define their required parameters (including nullable types).**
- **SimpleSignals supports multiple `SignalContexts` so you can constrain what signals are available in what contexts (if your application has become complex enough to require it).**

## Usage
Create an instance of a SignalManager that you want to register Signals and listners with. 

```
// Create an instance of the SignalManager
this.signalManager = this.AddComponent<SignalManager>();
SignalManager.RegisterSignalTypes(); // Optionally provde a SignalContext parameter
```

Next, define some Signals that you want to be able to `Invoke()` and that classes/components can listen to by providing a listener method. Becase Signals are strongly typed and they define the types of their parameters it makes it easy for programmers to know what parameters they need to impelment in their Signal Listener methods.

```
namespace MyGameNamespace 
{

    // Input Signals
    public class TapInputSignal : Signal{}
    public class SwipeInputSignal : Signal<Vector2,float>{} // SwipeVector, SwipeTime
}
```

I like to put all my Signal definitions in a single file. This provides an easy point of reference for someone new to the codebase to understand what's going on in the application. All they need to do is use the *Find References...* feature of their code editor and they can see all the places a Signal is invoked from or handeled by a listener.

To invoke a signal and notify its listeners you call `Invoke<SignalType>(...)`, in this example when the player lifts their finger we want to invoke a swipe Swipe Signal only if they've moved a minimum threshold. This could be an important distinction if the game performs one action on a tap and another on a swipe.

*Note: In this example the `GlobalContext` class is Singleton that holds a reference to the `SingleManager` instance we want to use when binding our classes/components Signal listeners. In your app, you can use any way of getting a reference to your `SignalManager` that makes sense in your codebase.*

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

Now you can write a component that listens to any of these signals, all you need to do is create a method with matching parameters and decorate it with a `[ListenTo]` attribute and register the component with the SignalManager using the `BindSignals()`

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
