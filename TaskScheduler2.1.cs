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
    partial class Program
    {
        /// <summary>
        /// A simple <see cref="Task"/> scheduler.
        /// </summary>
        public class TaskScheduler
        {
            /// <summary>
            /// Tasks currently managed by the <see cref="TaskScheduler"/> class.
            /// </summary>
            private readonly List<Task> TaskCollection = new List<Task>();

            public int MaxRunCount { get; private set; }
            public double RuntimeLimit { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="TaskScheduler"/> class.
            /// </summary>
            /// <param name="maxRunCount">Max allowed run count. Higher = slower.</param>
            /// <param name="runtimeLimit">Ms of runtime to keep the script under.</param>
            public TaskScheduler(int maxRunCount, double runtimeLimit)
            {
                Update(maxRunCount, runtimeLimit);
            }

            /// <summary>
            /// Updates some values in the class.
            /// </summary>
            /// <param name="runBatchSize">How many queued tasks to run in one tick.</param>
            /// <param name="runtimeLimit">Ms to keep the script runtime under.</param>
            public void Update(int runBatchSize, double runtimeLimit)
            {
                this.MaxRunCount = runBatchSize > 0 ? runBatchSize : 1;

                //if < 0, sets it to some big number that shouldn't ever be reached
                this.RuntimeLimit = runtimeLimit > 0 ? runtimeLimit : double.MaxValue;
            }

            /// <summary>
            /// Main execution loop of the <see cref="TaskScheduler"/> class.
            /// </summary>
            /// <param name="averageRuntime">Current average runtime ms.</param>
            public void Run(double averageRuntime = 0.0)
            {
                int i;
                int tasksInLine = 0;

                for (i = 0; i < TaskCollection.Count; i++)
                {
                    if (TaskCollection[i].shouldRun)
                    {
                        tasksInLine++;
                    }
                    else if (TaskCollection[i].Check(true))
                    {
                        tasksInLine++;
                    }
                }

                //int tasksInLine = TaskCollection.Count(t => t.shouldRun);

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
                            return;//exit method
                            break;//temp
                        }

                        taskToRun = TaskCollection.Find(t => t.shouldRun);
                    }
                }
            }

            /// <summary>
            /// Note: will throw an exception if <see cref="TaskCollection"/> contains no elements.
            /// </summary>
            /// <returns></returns>
            private Task GetHighestPrioTask()
            {
                Task taskToRun = TaskCollection[0];
                for (int i = 1; i < TaskCollection.Count; i++)
                {
                    if (TaskCollection[i].shouldRun && TaskCollection[i].WeightedPriority > taskToRun.WeightedPriority)
                    {
                        taskToRun = TaskCollection[i];
                    }
                }
                return taskToRun;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Task"/> class and adds it to <see cref="ActiveTasks"/>.
            /// </summary>
            /// <param name="Name">Task name.</param>
            /// <param name="Task"><see cref="IEnumerator"/><![CDATA[<]]><see cref="bool"/><![CDATA[>]]> for the task to run.</param>
            /// <param name="RunInterval">Run frequency. Higher is slower.</param>
            /// <param name="Priority">Runs the task with higher priority if two or more tasks attempt to run on the same tick.
            /// If there are multiple queued tasks with the same priority, they execute in order added.</param>
            public void AddTask(string Name, IEnumerable<bool> Task, int RunInterval, int Priority)
            {
                TaskCollection.Add(new Task(Name, Task, RunInterval, Priority));
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
                private int InternalTimer;

                public bool shouldRun { get; private set; }

                /// <summary>
                /// Uses the following formula: <see cref="Priority"/> / (<see cref="WaitTimeInTicks"/> / 30 + 1)
                /// </summary>
                public double WeightedPriority { get; private set; }
                private int WaitTimeInTicks;

                /// <summary>
                /// Initializes a new instance of the <see cref="Task"/> class.
                /// </summary>
                /// <param name="Name">Task name.</param>
                /// <param name="Task"><see cref="IEnumerable"/><![CDATA[<]]><see cref="bool"/><![CDATA[>]]> for the task to run.</param>
                /// <param name="RunInterval">Run frequency. Higher is slower. If 0, runs the task once.</param>
                /// <param name="Priority">Runs the task with higher priority if two or more tasks attempt to run on the same tick.
                /// If there are multiple queued tasks with the same priority, they execute in order added.</param>
                public Task(string Name, IEnumerable<bool> Task, int RunInterval, int Priority)
                {
                    this.Name = Name;
                    this.Job = new ITaskEnumerator<bool>(Task);
                    this.RunInterval = RunInterval;
                    this.InternalTimer = RunInterval;
                    this.Priority = Priority;
                    this.WeightedPriority = Priority;
                    this.WaitTimeInTicks = 0;

                    this.shouldRun = RunInterval == 0;
                }

                /// <summary>
                /// Runs the Task
                /// </summary>
                /// <returns><see cref="bool"/> hasMoreSteps</returns>
                public bool Run()
                {
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
                /// Checks if the task should run and updates <see cref="WeightedPriority"/>.
                /// </summary>
                /// <param name="moveTimer">Advances the internal clock if true.</param>
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
                public void Dispose()
                {
                    Job.Dispose();
                }
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

                public void Dispose()
                {
                    Enumerator.Dispose();
                }

                public bool MoveNext()
                {
                    return Enumerator.MoveNext();
                }

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
}
