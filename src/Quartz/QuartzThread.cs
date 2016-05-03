#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */

#endregion

using System.Threading;

namespace Quartz
{
    /// <summary>
    /// Support class used to handle threads
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public abstract class QuartzThread
    {
        /// <summary>
        /// The instance of System.Threading.Thread
        /// </summary>
        private readonly Thread thread;

        /// <summary>
        /// Initializes a new instance of the QuartzThread class
        /// </summary>
        protected QuartzThread() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Thread class.
        /// </summary>
        /// <param name="name">The name of the thread</param>
        protected QuartzThread(string name)
        {
            thread = new Thread(Run);
            Name = name;
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

#if THREAD_INTERRUPTION
        /// <summary>
        /// Interrupts a thread that is in the WaitSleepJoin thread state
        /// </summary>
        protected void Interrupt()
        {
            thread.Interrupt();
        }
#endif // THREAD_INTERRUPTION

        /// <summary>
        /// Gets or sets the name of the thread
        /// </summary>
        public string Name
        {
            get { return thread.Name; }
            protected set { thread.Name = value; }
        }

#if THREAD_PRIORITY
        /// <summary>
        /// Gets or sets a value indicating the scheduling priority of a thread
        /// </summary>
        protected ThreadPriority Priority
        {
            get { return thread.Priority; }
            set { thread.Priority = value; }
        }
#endif // THREAD_PRIORITY

        /// <summary>
        /// Gets or sets a value indicating whether or not a thread is a background thread.
        /// </summary>
        protected bool IsBackground
        {
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
        /// Obtain a string that represents the current object
        /// </summary>
        /// <returns>A string that represents the current object</returns>
        public override string ToString()
        {
#if THREAD_PRIORITY
            return $"Thread[{Name},{Priority},]";
#else // THREAD_PRIORITY
            return $"Thread[{Name}]";
#endif // THREAD_PRIORITY
        }
    }
}