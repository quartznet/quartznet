using System;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Serialization;
using System.Threading;

/// <summary>
/// This interface should be implemented by any class whose instances are intended 
/// to be executed by a thread.
/// </summary>
public interface IThreadRunnable
{
	/// <summary>
	/// This method has to be implemented in order that starting of the thread causes the object's 
	/// run method to be called in that separately executing thread.
	/// </summary>
	void Run();
}


/// <summary>
/// Contains conversion support elements such as classes, interfaces and static methods.
/// </summary>
public class SupportClass
{

	/// <summary>
	/// Holds the Property Port value
	/// </summary>
	private static int port;

	/// <summary>
	/// Property used to store and retrive the port used in a remote application
	/// </summary>
	public static int Port
	{
		get { return port; }

		set { port = value; }
	}

	/// <summary>
	/// Creates a new positive random number 
	/// </summary>
	/// <param name="random">The last random obtained</param>
	/// <returns>Returns a new positive random number</returns>
	public static long NextLong(Random random)
	{
		long temporaryLong = random.Next();
		temporaryLong = (temporaryLong << 32) + random.Next();
		if (random.Next(-1, 1) < 0)
		{
			return -temporaryLong;
		}
		else
		{
			return temporaryLong;
		}
	}

	/// <summary>
	/// Support class used to handle threads
	/// </summary>
	public class QuartzThread : IThreadRunnable
	{
		/// <summary>
		/// The instance of System.Threading.Thread
		/// </summary>
		private Thread thread;

		/// <summary>
		/// Initializes a new instance of the QuartzThread class
		/// </summary>
		public QuartzThread()
		{
			thread = new Thread(new ThreadStart(Run));
		}

		/// <summary>
		/// Initializes a new instance of the Thread class.
		/// </summary>
		/// <param name="Name">The name of the thread</param>
		public QuartzThread(string Name)
		{
			thread = new Thread(new ThreadStart(Run));
			this.Name = Name;
		}

		/// <summary>
		/// Initializes a new instance of the Thread class.
		/// </summary>
		/// <param name="Start">A ThreadStart delegate that references the methods to be invoked when this thread begins executing</param>
		public QuartzThread(ThreadStart Start)
		{
			thread = new Thread(Start);
		}

		/// <summary>
		/// Initializes a new instance of the Thread class.
		/// </summary>
		/// <param name="Start">A ThreadStart delegate that references the methods to be invoked when this thread begins executing</param>
		/// <param name="Name">The name of the thread</param>
		public QuartzThread(ThreadStart Start, string Name)
		{
			thread = new Thread(Start);
			this.Name = Name;
		}

		/// <summary>
		/// This method has no functionality unless the method is overridden
		/// </summary>
		public virtual void Run()
		{
		}

		/// <summary>
		/// Causes the operating system to change the state of the current thread instance to ThreadState.Running
		/// </summary>
		public void Start()
		{
			thread.Start();
		}

		/// <summary>
		/// Interrupts a thread that is in the WaitSleepJoin thread state
		/// </summary>
		public void Interrupt()
		{
			thread.Interrupt();
		}

		/// <summary>
		/// Gets the current thread instance
		/// </summary>
		public Thread Instance
		{
			get { return thread; }
			set { thread = value; }
		}

		/// <summary>
		/// Gets or sets the name of the thread
		/// </summary>
		public string Name
		{
			get { return thread.Name; }
			set
			{
				if (thread.Name == null)
				{
					thread.Name = value;
				}
			}
		}

		/// <summary>
		/// Gets or sets a value indicating the scheduling priority of a thread
		/// </summary>
		public ThreadPriority Priority
		{
			get { return thread.Priority; }
			set { thread.Priority = value; }
		}

		/// <summary>
		/// Gets a value indicating the execution status of the current thread
		/// </summary>
		public bool IsAlive
		{
			get { return thread.IsAlive; }
		}

		/// <summary>
		/// Gets or sets a value indicating whether or not a thread is a background thread.
		/// </summary>
		public bool IsBackground
		{
			get { return thread.IsBackground; }
			set { thread.IsBackground = value; }
		}

		/// <summary>
		/// Blocks the calling thread until a thread terminates
		/// </summary>
		public void Join()
		{
			thread.Join();
		}

		/// <summary>
		/// Blocks the calling thread until a thread terminates or the specified time elapses
		/// </summary>
		/// <param name="MiliSeconds">Time of wait in milliseconds</param>
		public void Join(long MiliSeconds)
		{
			lock (this)
			{
				thread.Join(new TimeSpan(MiliSeconds*10000));
			}
		}

		/// <summary>
		/// Blocks the calling thread until a thread terminates or the specified time elapses
		/// </summary>
		/// <param name="MiliSeconds">Time of wait in milliseconds</param>
		/// <param name="NanoSeconds">Time of wait in nanoseconds</param>
		public void Join(long MiliSeconds, int NanoSeconds)
		{
			lock (this)
			{
				thread.Join(new TimeSpan(MiliSeconds*10000 + NanoSeconds*100));
			}
		}

		/// <summary>
		/// Resumes a thread that has been suspended
		/// </summary>
		public void Resume()
		{
			thread.Resume();
		}

		/// <summary>
		/// Raises a ThreadAbortException in the thread on which it is invoked, 
		/// to begin the process of terminating the thread. Calling this method 
		/// usually terminates the thread
		/// </summary>
		public void Abort()
		{
			thread.Abort();
		}

		/// <summary>
		/// Raises a ThreadAbortException in the thread on which it is invoked, 
		/// to begin the process of terminating the thread while also providing
		/// exception information about the thread termination. 
		/// Calling this method usually terminates the thread.
		/// </summary>
		/// <param name="stateInfo">An object that contains application-specific information, such as state, which can be used by the thread being aborted</param>
		public void Abort(object stateInfo)
		{
			lock (this)
			{
				thread.Abort(stateInfo);
			}
		}

		/// <summary>
		/// Suspends the thread, if the thread is already suspended it has no effect
		/// </summary>
		public void Suspend()
		{
			thread.Suspend();
		}

		/// <summary>
		/// Obtain a string that represents the current object
		/// </summary>
		/// <returns>A string that represents the current object</returns>
		public override string ToString()
		{
			return "Thread[" + Name + "," + Priority.ToString() + "," + "" + "]";
		}

		/// <summary>
		/// Gets the currently running thread
		/// </summary>
		/// <returns>The currently running thread</returns>
		public static QuartzThread Current()
		{
			QuartzThread CurrentThread = new QuartzThread();
			CurrentThread.Instance = Thread.CurrentThread;
			return CurrentThread;
		}
	}

	/// <summary>
	/// This functions determines if the TimeZone uses daylight saving time
	/// </summary>
	/// <param name="timezone">TimeZone instance to validate</param>
	/// <returns>True or false depending if daylight savings time is used</returns>
	public static bool UseDaylightTime(TimeZone timezone)
	{
		return (timezone.DaylightName != "");
	}
}