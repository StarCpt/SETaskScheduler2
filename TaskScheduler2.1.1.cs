using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    /// <summary>
    /// A simple <see cref="Task"/> scheduler.
    /// </summary>
    public class TaskScheduler
    {
        public readonly List<Task> TaskCollection = new List<Task>();

        public int MaxRunCount { get; private set; }
        public double RuntimeLimit { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskScheduler"/> class.
        /// </summary>
        /// <param name="maxRunCount">How many eligible jobs to run in the same tick. Use 0 to set it to infinity.</param>
        /// <param name="runtimeLimit">Ms of runtime to keep the script under. Use 0 to disable.</param>
        public TaskScheduler(int maxRunCount = 0, double runtimeLimit = 0)
        {
            Update(maxRunCount, runtimeLimit);
        }

        /// <summary>
        /// Updates some values in the class.
        /// </summary>
        /// <param name="maxRunCount">How many eligible jobs to run in the same tick. Use 0 to disable</param>
        /// <param name="runtimeLimit">Ms of runtime to keep the script under. Use 0 to disable.</param>
        public void Update(int maxRunCount, double runtimeLimit)
        {
            this.MaxRunCount = maxRunCount > 0 ? maxRunCount : int.MaxValue;

            this.RuntimeLimit = runtimeLimit > 0 ? runtimeLimit : double.MaxValue;
        }

        /// <summary>
        /// Call this once per tick.
        /// </summary>
        /// <param name="averageRuntime">Current average runtime in ms.</param>
        public void Run(double averageRuntime = 0.0)
        {
            int i;
            int tasksInLine = 0;

            for (i = 0; i < TaskCollection.Count; i++)
            {
                if (TaskCollection[i].Check(true))
                {
                    tasksInLine++;
                }
            }

            if (tasksInLine == 0 || averageRuntime > RuntimeLimit)
            {
                return;
            }

            //TaskCollection.OrderBy(t => t.WeightedPriority);
            //Task taskToRun = TaskCollection.Find(t => t.shouldRun);

            Task taskToRun = GetHighestPrioTask();

            for (i = 0; i < MaxRunCount; i++)
            {
                bool hasMoreSteps = taskToRun.Run();

                if (!hasMoreSteps)
                {
                    if (taskToRun.RunInterval == 0)
                    {
                        TaskCollection.Remove(taskToRun);
                    }

                    tasksInLine--;

                    if (tasksInLine <= 0)
                    {
                        return;
                        break;//for testing
                    }

                    taskToRun = GetHighestPrioTask();
                }
                else if (!taskToRun.shouldRun)
                {
                    taskToRun = GetHighestPrioTask();
                }
            }
        }

        /// <summary>
        /// Note: will throw an exception if <see cref="TaskCollection"/> contains no elements.
        /// </summary>
        /// <returns></returns>
        private Task GetHighestPrioTask()
        {
            int firstShouldRunTaskOccurrenceIndex = TaskCollection.FindIndex(t => t.shouldRun);
            Task taskToRun = TaskCollection[firstShouldRunTaskOccurrenceIndex];
            for (; firstShouldRunTaskOccurrenceIndex < TaskCollection.Count; firstShouldRunTaskOccurrenceIndex++)
            {
                if (TaskCollection[firstShouldRunTaskOccurrenceIndex].shouldRun && TaskCollection[firstShouldRunTaskOccurrenceIndex].WeightedPriority > taskToRun.WeightedPriority)
                {
                    taskToRun = TaskCollection[firstShouldRunTaskOccurrenceIndex];
                }
            }
            return taskToRun;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Task"/> class and adds it to <see cref="TaskCollection"/>.
        /// </summary>
        /// <param name="Name">Task name.</param>
        /// <param name="Task">Coroutine to run.</param>
        /// <param name="RunInterval">Run frequency. ex: 1 = run every tick, 3 = run every 3 ticks. 0 to run the task once.</param>
        /// <param name="Priority">Task Priority. If there are multiple tasks with the same priority, they run in order added.</param>
        /// <param name="MultipleRuns">Allow the task to run again in the same tick.</param>
        public void AddTask(string Name, IEnumerable<bool> Task, int RunInterval, int Priority, bool MultipleRuns = false)
        {
            TaskCollection.Add(new Task(Name, Task, MathHelper.Clamp(RunInterval, 0, int.MaxValue), MathHelper.Clamp(Priority, 0, int.MaxValue), MultipleRuns));
        }

        /// <summary>
        /// <see cref="Task"/> class
        /// </summary>
        public class Task
        {
            public readonly string Name;
            private readonly ITaskEnumerator<bool> Job;
            public readonly int RunInterval;
            private readonly int Priority;
            public int InternalTimer { get; private set; }

            public bool shouldRun { get; private set; }

            /// <summary>
            /// Uses the following formula: <see cref="Priority"/> / (<see cref="WaitTimeInTicks"/> / 30 + 1)
            /// </summary>
            public double WeightedPriority { get; private set; }
            public int WaitTimeInTicks { get; private set; }
            private readonly bool MultipleRuns;

            /// <summary>
            /// Initializes a new instance of the <see cref="Task"/> class.
            /// </summary>
            /// <param name="Name">Task name.</param>
            /// <param name="Task"><see cref="IEnumerable"/><![CDATA[<]]><see cref="bool"/><![CDATA[>]]> for the task to run.</param>
            /// <param name="RunInterval">Run frequency. Higher is slower. If 0, runs the task once.</param>
            /// <param name="Priority">Runs the task with higher priority if two or more tasks attempt to run on the same tick.
            /// If there are multiple queued tasks with the same priority, they execute in order added.</param>
            /// <param name="MultipleRuns">Allow the task to run more than once in the same tick.</param>
            public Task(string Name, IEnumerable<bool> Task, int RunInterval, int Priority, bool MultipleRuns)
            {
                this.Name = Name;
                this.Job = new ITaskEnumerator<bool>(Task);
                this.RunInterval = RunInterval;
                this.InternalTimer = RunInterval;
                this.Priority = Priority;
                this.WeightedPriority = Priority;
                this.WaitTimeInTicks = 0;
                this.MultipleRuns = MultipleRuns;

                this.shouldRun = RunInterval == 0;
            }

            /// <summary>
            /// Runs the Task
            /// </summary>
            /// <returns><see cref="bool"/> hasMoreSteps</returns>
            public bool Run()
            {
                shouldRun = MultipleRuns;

                WaitTimeInTicks = 0;
                InternalTimer = RunInterval;
                WeightedPriority = Priority;

                bool hasMoreSteps = Job.MoveNext();

                if (!hasMoreSteps)
                {
                    Job.Dispose();

                    if (RunInterval != 0)
                    {
                        Job.Reset();
                    }

                    shouldRun = false;
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Checks if the task should run and updates <see cref="WeightedPriority"/>
            /// </summary>
            /// <param name="moveTimer">Advances the internal clock if eligible</param>
            /// <returns><see cref="bool"/> shouldRun</returns>
            public bool Check(bool moveTimer)
            {
                if (moveTimer && InternalTimer > 0)
                {
                    InternalTimer--;
                }

                if (InternalTimer <= 0)
                {
                    if (moveTimer)
                    {
                        WaitTimeInTicks++;
                        WeightedPriority = Priority / (WaitTimeInTicks / 300d + 1d);
                    }

                    shouldRun = true;
                    return true;
                }

                shouldRun = false;
                return false;
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            public void Dispose() => Job.Dispose();
        }

        /// <summary>
        /// Resettable <see cref="IEnumerator{T}"/>
        /// </summary>
        public class ITaskEnumerator<T> : IEnumerator<T>
        {
            private readonly IEnumerable<T> Enumerable;
            private IEnumerator<T> Enumerator;

            public ITaskEnumerator(IEnumerable<T> Enumerable)
            {
                this.Enumerable = Enumerable;
                this.Enumerator = Enumerable.GetEnumerator();
            }

            public T Current
            {
                get
                {
                    return Enumerator.Current;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            public void Dispose() => Enumerator.Dispose();

            public bool MoveNext() => Enumerator.MoveNext();

            /// <summary>
            /// Can be called after <see cref="Dispose"/>. Resets the Enumerator to the beginning.
            /// </summary>
            public void Reset()
            {
                Enumerator = Enumerable.GetEnumerator();
            }
        }
    }
}
