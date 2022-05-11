# Initialize the manager:
```TaskScheduler scheduler = new TaskScheduler(MaxRunCount, RuntimeLimit);```
- ```MaxRunCount```: how many 'steps' to move. could be 1 step in 5 different coroutines, could be 5 in 1 coroutine. depends on what the "MultipleRuns" setting is. set it to 0 if you dont want to limit it
- ```RuntimeLimit```: if the input runtime in Run() is over this number, skips that tick

# Add coroutines to the manager:
```scheduler.AddTask(Name, Coroutine, RunInterval, Priority, MultipleRuns);```
- ```Name```: can be any string
- ```Coroutine```: has to be an IEnumerable<bool>
- ```Priority```: Priority. If there are multiple tasks with the same priority, they run in order added
- ```MultipleRuns```: Allow the task to run more than once in the same tick if it has steps left

# Run the manager:
```scheduler.Run(AverageRuntime);```
- ```AverageRuntime```: the average runtime of the script. up to you to get that