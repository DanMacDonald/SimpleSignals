/*
Copyright (c) 2019 Dan MacDonald

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation 
files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the 
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE 
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR 
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, 
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using UnityEngine;
using System.Collections.Generic;
using System;
using System.Reflection;
using System.Linq;

// From https://github.com/DanMacDonald/SimpleSignals/

namespace SimpleSignals
{
	///<summary>
	/// Base class for the host application to derive from to register signal instances
	/// to be bound to and invoked.
	///</summary>
	public class SignalContext
	{
		// Cache of all registered signal types
		private Dictionary<Type, ISignal> signalByType = new Dictionary<Type,ISignal>();
		
		///<summary>
		/// Registers a signal type with this context
		///</summary>
		public void RegisterSignal<T>() where T : ISignal, new()
		{
			Type signalType = typeof(T);
			this.RegisterSignal(signalType);
		}

		///<summary>
		/// Registers a signal type with this context
		///</summary>
		public void RegisterSignal(Type signalType)
		{
			//Debug.Log("RegisteringSignal Type:" + signalType);
			// Is this a new Signal type?
			if(this.signalByType.ContainsKey(signalType) == false)
			{	
				// Create an instance of the signal
				ISignal signalInstance = (ISignal)Activator.CreateInstance(signalType);
				
				// Store a reference to the signalInstance by its Type
				this.signalByType.Add(signalType, signalInstance);	
			}
		}
		
		///<summary>
		/// Gets a signal instance from the context by type, usually to invoke it
		///</summary>
		public T GetSignal<T>() where T : ISignal
		{
			Type signalType = typeof(T);
			// Cast the ISignal instance back to T, and return it
			return (T) this.GetSignal(signalType);
		}
		
		///<summary>
		/// Gets a signal ISignal reference from the context by signal type, used by the Signal
		/// Manager when binding signals to delegate listeners.
		///</summary>
		public ISignal GetSignal(Type signalType)
		{
			ISignal signalInstance = null;
			
			// Is this signal type known to us?
			if(this.signalByType.ContainsKey(signalType))
			{
				// Get a reference to the existing instance
				signalInstance = this.signalByType[signalType];
			}

			return signalInstance;
		}
		
		///<summary>
		/// Called by the SignalManager after it instantiates a default SignalContext instance to contain
		/// all the signal types discoverd in the assembly.
		///</summary>
		public void Register()
		{
			// Get a list of all the signal types defind in the assembly
			List<Type> typeList = GetAllSubclassesOf(typeof(Signal));
			foreach(Type type in typeList)
			{
				if(type.Namespace == "SimpleSignals") 
				{
					// Ignore the built in signal types
					continue;
				}
				else
				{
					// Register any derived signal types
					this.RegisterSignal(type);
				}
			}
		}

		///<summary>
		/// Virtual method that can be overridden in a derived SignalContext class that enables manual signal registration
		public virtual void OnRegister()
		{

		}

		///<summary>
		/// Helper method that lists all the subclasses / derived types of a base type
		/// This is how SimpleSignals discovers what Signals the project has defined
		///</summary>
		private List<Type> GetAllSubclassesOf(Type baseType) 
		{ 
			return AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes()).Where(type => type.IsSubclassOf(baseType)).ToList();
		}
	}
	
	///<summary>
	/// Responsible for binding and unbinding objects to the signals cached in the SignalContext
	///</summary>
	public class SignalManager : MonoBehaviour
	{
		// Instance of an object containing Signal types this SignalManager is managing
		public SignalContext SignalContext {get; private set; }

		/// <summary>
		/// Internal struct for mappling a specific Signal field on the SignalSourceObject to a delegate  
		/// that binds it to a method on the listener object.
		/// </summary>
		private struct SignalListener
		{
			public SignalListener(ISignal signal, Delegate listener) 
			{ 
				this.Signal = signal; 
				this.Listener = listener; 
			}
			public ISignal Signal;
			public Delegate Listener;
		}

		// Stores all the listener delegates a given object has registered to signals in the SignalContext object
		private Dictionary<System.Object, List<SignalListener>> signalListenersByObject = new Dictionary<System.Object, List<SignalListener>>();

		// MethodInfo cache so we aren't slow every time we register callbacks for an object
		private Dictionary<Type, IEnumerable<MethodInfo>> methodInfoCache = new Dictionary<Type, IEnumerable<MethodInfo>>();

		// List of objects to unbind, this enables the signal manager to unbind objects during an Invoke(), without breaking collection enumeration
		private List<System.Object> objectsToUnbind = new List<System.Object>(1);
		private bool isInvoking = false;

		///<summary>
		/// Assigns a SignalContext to this SignalManager instance and calls its OnRegister() method.
		///</summary>
		public void RegisterSignalTypes(SignalContext signalContext=null)
		{
			if (this.SignalContext != null)
				UnbindAllSignalListeners();

			if(signalContext != null)
			{
				// Register only those Signals defined by the context
				this.SignalContext = signalContext;
				this.SignalContext.OnRegister();
			}
			else
			{
				// Automatically discover and register all the Signals found in the project
				this.SignalContext = new SignalContext();
				this.SignalContext.Register();
			}
		}

		/// <summary>
		/// When the SignalManager is boud to a new SignalContext, we need to
		/// be able to unbind all the listeners bound to the existing SignalContext.
		/// This frees all the existing listener references so we don't accumulate
		/// references for signals that are no longer rgistered or have thoes orphaned
		/// references holding onto their listener objects preventing GarbageCollection.
		/// </summary>
		void UnbindAllSignalListeners()
		{
			foreach(var listenerObj in signalListenersByObject.Keys)
			{
				objectsToUnbind.Add(listenerObj);
			}

			foreach (var obj in this.objectsToUnbind)
			{
				this.UnbindSignals(obj);
			}
			objectsToUnbind.Clear();
			signalListenersByObject.Clear();
		}

		/// <summary>
		/// This is the workhorse method for SignalManager at runtime. 
		/// All signals get Invoked() though this entry point.
		/// </summary>
		public void Invoke<T>(params object[] list) where T : Signal
		{
			isInvoking = true;
			Signal signal = this.GetSignal<T>();

			if (signal == null)
				throw new InvalidOperationException($"The Signal `{ typeof(T).Name }` is not registered with the SignalMangers SignalContext. Please register the signal before invoking it." );

			if(signal.ParameterCount == 0)
			{
				if (list != null && list.Length != 0)
				{
					isInvoking = false;
					throw new ArgumentException ($"Incorrect number of arguments passed to 'Invoke<{ signal.GetType().Name }>(...)'. Expected 0 arguments but you provided {list.Length} (or null).");
				}

				signal.Invoke(this);
			}
			else
			{
				// Handle a special case for Signals with a single parameter, in that case if the parameter
				// is a nullable type, and it is invoked with "null", the argument list will be null as well.
				if ((list == null && signal.ParameterCount != 1)
				|| (list != null && list.Length != signal.ParameterCount))
				{	
					var argumentCount = list == null ? 0 : list.Length;
					isInvoking = false;
					throw new ArgumentException ($"Incorrect number of arguments passed to 'Invoke<{ signal.GetType().Name }>(...)'. Expected {signal.ParameterCount} argument(s) but you provided {argumentCount} (or null).");
				}

				try
				{
					((T)signal).Invoke(this, list);
				}
				catch(Exception)
				{
					isInvoking = false;
					var methodBase = new System.Diagnostics.StackFrame(0).GetMethod();
					if (methodBase.Name == "Invoke" && list != null)
					{
						// For any exception that hapend during our Invoke() call (but not whatever it invoked), catch it
						// and see if it was something to do with parameter type missmatch.
						var typeMissmatchMessage = string.Empty;
						var argumentTypes = new Type [list.Length];
						for(int i=0; i<list.Length; i++)
						{
							argumentTypes[i] = list[i] == null ? typeof(object) : list[i].GetType();
						}

						var parameterIndex = 0;
						if (signal.TryGetParameterErrorMessage(argumentTypes, out typeMissmatchMessage, out parameterIndex))
							throw new ArgumentException($"Incorrect argument type passed to `Invoke<{ signal.GetType().Name }>(...)`. { typeMissmatchMessage } The { ToOrdinal(parameterIndex+1) } argument of the Invoke(...) does not match what is defined by '{ signal.GetType().Name }'.");
						else
							throw;
					}
					else
					{
						// If there was an InvalidCastException that occured inside the body of the listener method,
						// allow it to flow back to the host application unchanged. This preserves the line number and
						// target site of the exception.
						throw;
					}
				}
			}
			isInvoking = false;

			// If objects were unbound during the invoke, now's the time to unbind them
			if (this.objectsToUnbind.Count > 0 )
			{
				foreach (var obj in this.objectsToUnbind)
				{
					this.UnbindSignals(obj);
				}
				objectsToUnbind.Clear();
			}
		}

		///<summary>
		/// Helper function that binds the methods decorated with ListenTo attributes in the listener object
		/// to the corresponding Signal instances in the SignalContext assigned to the signalManager.
		///</summary>
		static public SignalManager BindSignals(SignalManager signalManager, System.Object listenerObject)
		{
			if(signalManager != null)
			{
				signalManager.BindSignals(listenerObject);
				return signalManager;
			}
			else
			{
				throw new ArgumentException("Attempting to bind signals to a null signalManager");
			}
		}

		///<summary>
		/// Helper function that unbinds any signal listeners in the listenerObject from any Signal instances
		/// in the SignalContext assigned to the specified signalManager.
		///</summary>
		static public void UnbindSignals(SignalManager signalManager, System.Object listenerObject)
		{
			if(signalManager != null)
			{
				if (signalManager.isInvoking)
				{
					signalManager.objectsToUnbind.Add(listenerObject);
				}
				else
				{
					signalManager.UnbindSignals(listenerObject);
				}
			}
		}
		
		///<summary>
		/// Convienence method that attemts to get an instance of a signal registered with the SignalContext by type.
		///</summary>
		public T GetSignal<T>() where T : ISignal
		{
			ISignal signal = null;
			if(this.SignalContext != null)
			{
				signal = this.SignalContext.GetSignal<T>();
			}
			
			return (T)signal;
		}

		/// <summary>
		/// Inspects the listenerObject for methods decorated with the [ListenTo] attribute.
		/// Then, Creates a delegate for each of those methods and binds it to the corresponding
		/// Signal instance registered with the SignalContext.
		/// Objects that autobind their listeners this way must call UnbindSignals()
		/// before they are destroyed.
		/// </summary>
		protected void BindSignals(System.Object listenerObject)
		{
			// Try to retrieve MethodInfo's for the targetObject from a cache first, cuz reflection is slow
			IEnumerable<MethodInfo> methodInfos = null;
			Type listenerObjectType = listenerObject.GetType();
			if(this.methodInfoCache.TryGetValue(listenerObjectType, out methodInfos) == false)
			{
				// If you REEAALLLLY have to, use reflection to look up the methodInfos and cache them
				methodInfos = listenerObjectType.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
				this.methodInfoCache[listenerObjectType] = methodInfos;
			}

			// Loop though all the methodInfos on the targetObject...
			foreach(MethodInfo methodInfo in methodInfos)
			{
				// Identify listener methods by retrieving a SignalListenerAttribute associated with the methodInfo
				ListenTo[] listenerBindings = null;
				listenerBindings = (ListenTo[])methodInfo.GetCustomAttributes(typeof(ListenTo), false);

				// Should be only one ListenToAttribute since they are exclusive (if there is one at all)
				foreach(ListenTo listenTo in listenerBindings)
				{
                    // Debug.Log("ListenToAttribute:" + listenTo.SignalType);
					// Get an ISignal reference from the signalContext based on the SignalType
					ISignal signal = this.SignalContext.GetSignal(listenTo.SignalType);
					if(signal != null)
					{
						var methodParameterCount = methodInfo.GetParameters().Length;
						if (methodParameterCount != signal.ParameterCount)
							throw new ArgumentException( $"Incorrect number of parameters found when binding [ListenTo(typeof({ signal.GetType().Name })]. Expected to find {signal.ParameterCount} parameter(s) but found { methodParameterCount }.");

						Delegate d = null;
						try
						{
							// Create a callback listener for this methodInfo
							d = Delegate.CreateDelegate(signal.GetListenerType(), listenerObject, methodInfo);
						}
						catch (ArgumentException)
						{
							var parameters = methodInfo.GetParameters();
							var parameterTypes = new Type [parameters.Length];
							for(int i=0; i<parameters.Length; i++)
							{
								parameterTypes[i] = parameters[i].ParameterType;
							}

							var typeMissmatchMessage = string.Empty;
							var parameterIndex = 0;
							if (signal.TryGetParameterErrorMessage(parameterTypes, out typeMissmatchMessage, out parameterIndex))
								throw new ArgumentException($"Incorrect parameter type while binding listener method `{ methodInfo.DeclaringType.Name }.{ methodInfo.Name }`. { typeMissmatchMessage } The { ToOrdinal(parameterIndex+1) } parameter in the listener method does not match what is defined by the Signal.");
							else
								throw;
						}

						signal.AddListener(d, listenTo.ListenerType);

						// Add the signal listener to the internal list of delegates associated with the listenerObject
						// ( so we can easily unbind all the signal listeners later. )
						List<SignalListener> delegateList = null;
						if(this.signalListenersByObject.TryGetValue(listenerObject, out delegateList) == false)
						{
							//Debug.Log("adding signal listener:" + methodInfo.Name + " for " + listenerObjectType);
							delegateList = new List<SignalListener>();
							this.signalListenersByObject[listenerObject] = delegateList;
						}
						delegateList.Add(new SignalListener(signal, d));
					}
					else
					{
						throw new InvalidOperationException($"Unable to Bind Singals for an instance of '{ listenerObjectType }'. The Signal '{ listenTo.SignalType }' is not registered with the SignalManager.");
					}
				}
			}
		}

		/// <summary>
		/// Looks up all the callback listeners created for this object and removes
		/// them from their corresponding CallbackSources effectively unregistering them.
		/// </summary>
		protected void UnbindSignals(System.Object listenerObject)
		{
			this.RemoveListeners(listenerObject);
			this.signalListenersByObject.Remove(listenerObject);
		}

		/// <summary>
		/// Removes all the listenerObjects delegate listeners from the corresponding Signals
		/// in the SignalContext
		/// </summary>
		private void RemoveListeners(System.Object listenerObject)
		{
			List<SignalListener> delegateList = null;

			if(this.signalListenersByObject.TryGetValue(listenerObject, out delegateList))
			{
				foreach(SignalListener data in delegateList)
				{
					data.Signal.RemoveListener(data.Listener);
				}
			
				delegateList.Clear();
			}
		}

		/// <summary>
		/// Annoying to have to write, but it pretties up error messages.
		/// </summary>
		private string ToOrdinal(int num)
		{
			switch(num % 100) { case 11: case 12: case 13: return num.ToString() + "th"; }

			switch(num % 10)
			{
				case 1: return num.ToString() + "st";
				case 2: return num.ToString() + "nd";
				case 3: return num.ToString() + "rd";
				default:
					return num.ToString() + "th";
			}
		}
	}

	/// <summary>
	/// Custom "ListenTo" attribute that enables auto mapping of handler methods to SignalSources
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class ListenTo : Attribute
	{
		public Type SignalType { get; private set; }
		public ListenerType ListenerType {get; private set; }

		public ListenTo(Type signalType, ListenerType listenerType = ListenerType.Every)
		{
			if (signalType == null) 
			{ 
				throw new InvalidOperationException ("Cannont bind [ListenTo(null)] attribute, you must provide a valid Signal Type to listen to. For example [ListenTo(typeof(MySignal))]."); 
			}

			this.SignalType = signalType;
			this.ListenerType = listenerType;
			if(typeof(ISignal).IsAssignableFrom(this.SignalType) == false)
			{
				throw new InvalidOperationException($"Attempted to ListenTo a type that doesn't derive from Signal. Did you mean to [ListenTo(typeof({ this.SignalType.Name })] ?");
			}
		}
	}

	public enum ListenerType
	{
		Every, Once
	}

	public struct SignalListenerItem
	{
		public Delegate SignalDelegate;
		public ListenerType ListenerType;
	}

	public interface ISignal
	{
		void AddListener(Delegate d, ListenerType t);
		void RemoveListener(Delegate d);
		Type GetListenerType();
		bool TryGetParameterErrorMessage(Type [] paramsToValidate, out string typeMissmatchError, out int parameterIndex);
		int ParameterCount { get; }
	}

	// Base/Simple Signal with no parameters 
	public class Signal : ISignal
	{
		public delegate void SignalDelegate();
		virtual public int ParameterCount { get { return 0;} }
		protected List<SignalListenerItem> listeners = new List<SignalListenerItem> ();
		protected List<SignalListenerItem> listenersToRemove = new List<SignalListenerItem>();

		public virtual bool TryGetParameterErrorMessage(Type [] paramsToValidate, out string typeMissmatchMessage, out int parameterIndex)
		{
			return TryGetParameterErrorMessage(paramsToValidate, new Type[] {}, out typeMissmatchMessage, out parameterIndex);
		}

		protected bool TryGetParameterErrorMessage(Type [] paramsToValidate, Type [] signalParameters, out string typeMissmatchMessage, out int parameterIndex)
		{
			parameterIndex = default(int);
			typeMissmatchMessage = string.Empty;
			
			for(int i = 0; i < paramsToValidate.Length; i++)
			{
				parameterIndex = i;
				var listenerParameterType = paramsToValidate[i];
				var signalParameterType = signalParameters[i];

				if (!listenerParameterType.IsAssignableFrom(signalParameterType)
				|| listenerParameterType.IsValueType != signalParameterType.IsValueType)
				{
					var foundName = listenerParameterType.Name == "Object" ? "null" : listenerParameterType.Name;
					typeMissmatchMessage = $"Expected '{ signalParameterType.Name }' but found '{ foundName }'.";
					return true;
				}
			}
			return false;
		}

		public void Invoke(SignalManager signalManager) 
		{ 
			foreach(var listener in listeners)
			{ 
				if(ValidateSignalListener(signalManager, listener))
			 		((SignalDelegate)listener.SignalDelegate).Invoke(); 
			} 

			RemoveDiscardedSignalListenerItems();
		}

		virtual public void Invoke(SignalManager signalManager, params object[] list)
		{
			this.Invoke(signalManager); 
		}

		public void AddListener(Delegate listener, ListenerType listenerType = ListenerType.Every) 
		{ 
			foreach(var listenerItem in this.listeners)
			{
				if(listenerItem.SignalDelegate.Target == listener.Target && listenerItem.SignalDelegate.Method == listener.Method)
					throw new Exception($"Attempted to add a duplicate { listener.Method.Name } listener for { listener.Target }");
			}
			InternalAdd(listener, listenerType);
		}

		virtual protected void InternalAdd(Delegate listener, ListenerType listenerType)
		{
			this.listeners.Add(new SignalListenerItem() { SignalDelegate=(SignalDelegate)listener, ListenerType=listenerType});
		}

		/// <summary>
		/// Note: This method validates that the target listener object is still around, if the
		/// listener object is null, it removes any delegates bound to that object. This is the 
		/// only part of the code that is Unity specific. If you'd like to us SimpleSignals in 
		/// a vanilla C# enviornment, this is the only method that needs to be tweaked.
		/// </summary>
		protected bool ValidateSignalListener(SignalManager signalManager, SignalListenerItem listener)
		{
			bool isInvokable = true;
			object target = listener.SignalDelegate.Target;

			// <UnitySpecific>
			// Comment this code out if you're not using MonoBehaviors
			if (target is MonoBehaviour)
			{
				MonoBehaviour targetGO = (MonoBehaviour)target;

				// Don't invoke listeners on destroyed GameObjects instead Clean up listeners
				if( targetGO == null && !ReferenceEquals(targetGO, null))
				{
					isInvokable = false;
					SignalManager.UnbindSignals(signalManager, targetGO);
				}
			}
			// </UnitySpecific>

			// This listener delegate will be removed after it's invoked
			if(listener.ListenerType == ListenerType.Once)
				this.listenersToRemove.Add(listener);

			return isInvokable;
		}

		protected void RemoveDiscardedSignalListenerItems()
		{
			foreach(var itemToRemove in this.listenersToRemove)
			{
				//Debug.Log("Removing discarded signal " + itemToRemove.SignalDelegate.Method.Name);
				this.listeners.Remove(itemToRemove);
			}
			this.listenersToRemove.Clear();
		}

		public void RemoveListener(Delegate listener) 
		{ 
			SignalListenerItem itemToRemove = new SignalListenerItem();

			foreach(var listenerItem in listeners)
			{
				if(listenerItem.SignalDelegate == listener)
				{
					itemToRemove = listenerItem;
					break;
				}
			}
			
			if(itemToRemove.SignalDelegate != null)
				this.listeners.Remove(itemToRemove);
		}
		virtual public Type GetListenerType() { return typeof(SignalDelegate); }
	}
	
	// Signal with 1 parameter
	public class Signal<T> : Signal, ISignal
	{
		new public delegate void SignalDelegate(T param1);
		override public int ParameterCount { get { return 1;} }
		override public bool TryGetParameterErrorMessage(Type [] paramToValidate, out string typeMissmatchError, out int parameterIndex)
		{
			return TryGetParameterErrorMessage(paramToValidate, new Type[] { typeof(T) }, out typeMissmatchError, out parameterIndex);
		}
		public void Invoke(SignalManager signalManager, T param1) 
		{ 
			foreach(var listener in listeners)
			{ 
				if(ValidateSignalListener(signalManager, listener))
			 		((SignalDelegate)listener.SignalDelegate).Invoke(param1); 
			}

			RemoveDiscardedSignalListenerItems();
		}
		override public void Invoke(SignalManager signlaManager, params object[] list) 
		{
			if (list != null)
			{
				this.Invoke(signlaManager, (T)list[0]); 
			}
			else 
			{
				var t = typeof(T);
				if (t.IsValueType == false)
					this.Invoke(signlaManager, default(T));
				else
					throw new ArgumentException($"Incorrect argument type passed to `Invoke<{ this.GetType().Name }>(...)`. Expected `{ t.Name }` but found 'null'. The argument type provided to Invoke(...) does not match what is defined by '{ this.GetType().Name }'.");
				
			}
		}
		override protected void InternalAdd(Delegate listener, ListenerType listenerType){ this.listeners.Add(new SignalListenerItem() { SignalDelegate=(SignalDelegate)listener, ListenerType=listenerType});}
		override public Type GetListenerType() { return typeof(SignalDelegate); }
	}

	// Signal with 2 parameters
	public class Signal<T1,T2> : Signal, ISignal
	{
		new public delegate void SignalDelegate(T1 param1,T2 param2);
		override public int ParameterCount { get { return 2;} }
		public void Invoke(SignalManager signalManager, T1 param1,T2 param2) 
		{ 
			foreach(var listener in listeners)
			{ 
				if(ValidateSignalListener(signalManager, listener)) 
					((SignalDelegate)listener.SignalDelegate).Invoke(param1,param2); 
			}
			RemoveDiscardedSignalListenerItems();
		}
		override public bool TryGetParameterErrorMessage(Type [] paramsToValidate, out string typeMissmatchError, out int parameterIndex) { return TryGetParameterErrorMessage(paramsToValidate, new Type[] { typeof(T1), typeof(T2) }, out typeMissmatchError, out parameterIndex); }		
		override public void Invoke(SignalManager signalManager, params object[] list) { this.Invoke(signalManager, (T1)list[0], (T2)list[1]); }
		override protected void InternalAdd(Delegate listener, ListenerType listenerType){ this.listeners.Add(new SignalListenerItem() { SignalDelegate=(SignalDelegate)listener, ListenerType=listenerType});}
		override public Type GetListenerType() { return typeof(SignalDelegate); }
	}

	// Signal with 3 parameters	
	public class Signal<T1,T2,T3> : Signal, ISignal
	{
		new public delegate void SignalDelegate(T1 param1,T2 param2,T3 param3);
		override public int ParameterCount { get { return 3;} }
		public void Invoke(SignalManager signalManager, T1 param1, T2 param2, T3 param3) 
		{ 
			foreach(var listener in listeners)
			{ 
				if(ValidateSignalListener(signalManager, listener)) 
					((SignalDelegate)listener.SignalDelegate).Invoke(param1,param2,param3); 
			}
			RemoveDiscardedSignalListenerItems();
		}
		override public bool TryGetParameterErrorMessage(Type [] paramsToValidate, out string typeMissmatchError, out int parameterIndex) { return TryGetParameterErrorMessage(paramsToValidate, new Type[] { typeof(T1), typeof(T2), typeof(T3) }, out typeMissmatchError, out parameterIndex); }
		override public void Invoke(SignalManager signalManager, params object[] list) { this.Invoke(signalManager, (T1)list[0], (T2)list[1], (T3)list[2]); }
		override protected void InternalAdd(Delegate listener, ListenerType listenerType){ this.listeners.Add(new SignalListenerItem() { SignalDelegate=(SignalDelegate)listener, ListenerType=listenerType});}
		override public Type GetListenerType() { return typeof(SignalDelegate); }
	}

	// Signal with 4 parameters	
	public class Signal<T1,T2,T3,T4> : Signal, ISignal
	{
		new public delegate void SignalDelegate(T1 param1,T2 param2,T3 param3,T4 param4);
		override public int ParameterCount { get { return 4;} }
		public void Invoke(SignalManager signalManager, T1 param1,T2 param2,T3 param3,T4 param4) 
		{ 
			foreach(var listener in listeners)
			{ 
				if(ValidateSignalListener(signalManager, listener)) 
					((SignalDelegate)listener.SignalDelegate).Invoke(param1,param2,param3,param4); 
			}
			RemoveDiscardedSignalListenerItems();
		}
		override public bool TryGetParameterErrorMessage(Type [] paramsToValidate, out string typeMissmatchError, out int parameterIndex) { return TryGetParameterErrorMessage(paramsToValidate, new Type[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) }, out typeMissmatchError, out parameterIndex); }
		override public void Invoke(SignalManager signalManager, params object[] list){ this.Invoke(signalManager, (T1)list[0], (T2)list[1], (T3)list[2], (T4)list[3]); }
		override protected void InternalAdd(Delegate listener, ListenerType listenerType){ this.listeners.Add(new SignalListenerItem() { SignalDelegate=(SignalDelegate)listener, ListenerType=listenerType});}
		override public Type GetListenerType() { return typeof(SignalDelegate); }
	}
}