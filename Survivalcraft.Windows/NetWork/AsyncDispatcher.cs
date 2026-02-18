using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Game;

public static class AsyncDispatcher
{
    private static ConcurrentQueue<ITask> actions = new ConcurrentQueue<ITask>();
    private static List<ITask> toAdd = new List<ITask>();

    public interface ITask
    {
        bool Update();
    }

    public class WaitUntil : ITask
    {
        Func<bool> predicate;
        Action action;

        public WaitUntil(Func<bool> predicate, Action action)
        {
            this.predicate = predicate;
            this.action = action;
        }

        public bool Update()
        {
            if (predicate())
            {
                action?.Invoke();
                return true;
            }
            return false;
        }
    }

    public class SimpleTask : ITask
    {
        Action action;

        public SimpleTask(Action action)
        {
            this.action = action;
        }

        public bool Update()
        {
            action?.Invoke();
            return true;
        }
    }

    public class RepeatTask : ITask
    {
        Func<bool> func;

        public RepeatTask(Func<bool> func)
        {
            this.func = func;
        }

        public bool Update() => func();
    }

    public class Task : ITask
    {
        IEnumerator<bool> generator;
        Task next;

        public Task(IEnumerator<bool> generator)
        {
            this.generator = generator;
        }

        public bool Update()
        {
            if (!generator.MoveNext() || generator.Current)
            {
                if (next != null)
                {
                    generator = next.generator;
                    next = next.next;
                }
                else
                {
                    return true;
                }
            }
            return false;
        }

        public Task ContinueWith(IEnumerator<bool> generator)
        {
            next = new Task(generator);
            return next;
        }
    }

    public static void Update()
    {
        if (ScreensManager.CurrentScreen == null) return;

        while (actions.TryDequeue(out ITask tsk))
        {
            try
            {
                if (!tsk.Update())
                {
                    toAdd.Add(tsk);
                }
            }
            catch
            {
            }
        }

        foreach (var tsk in toAdd)
        {
            Dispatch(tsk);
        }
        toAdd.Clear();
    }

    public static void Dispatch(Action action)
    {
        Dispatch(new SimpleTask(action));
    }

    public static void Dispatch(Func<IEnumerator<bool>> func)
    {
        Dispatch(func());
    }

    public static void Dispatch(IEnumerator<bool> action)
    {
        Dispatch(new Task(action));
    }

    public static void Dispatch(Func<bool> func)
    {
        Dispatch(new RepeatTask(func));
    }

    public static void Dispatch(ITask task)
    {
        actions.Enqueue(task);
    }
}
