var lockA = new object();
var lockB = new object();

var t1 = new Thread(() =>
{
    lock (lockA)
    {
        Console.WriteLine("T1 acquired lock A");
        Thread.Sleep(100);
        Console.WriteLine("T1 trying to acquire lock B");
        lock (lockB) { Console.WriteLine("T1 acquired both locks"); }
    }
});

var t2 = new Thread(() =>
{
    lock (lockB)
    {
        Console.WriteLine("T2 acquired lock B");
        Thread.Sleep(100);
        Console.WriteLine("T2 trying to acquire lock A");
        lock (lockA) { Console.WriteLine("T2 acquired both locks"); }
    }
});

t1.Start();
t2.Start();

Thread.Sleep(200);
Console.WriteLine($"\nDeadlockApp running (PID {Environment.ProcessId}) — waiting forever.");
Console.WriteLine($"Create a dump with:   dotnet dump collect -p {Environment.ProcessId}");
t1.Join();
