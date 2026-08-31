namespace Nav.Core.Tests;

public sealed class BinaryHeapTests
{
    [Fact]
    public void PopsInAscendingOrderOfF()
    {
        var heap = new BinaryHeap();
        foreach (var f in new[] { 5.0, 1.0, 4.0, 2.0, 3.0 })
        {
            heap.Push((int)f, f, 0.0);
        }

        var popped = new List<int>();
        while (heap.Count > 0)
        {
            popped.Add(heap.Pop());
        }

        Assert.Equal([1, 2, 3, 4, 5], popped);
    }

    [Fact]
    public void EqualFIsSettledByLowerH()
    {
        var heap = new BinaryHeap();
        heap.Push(cell: 10, f: 7.0, h: 5.0);
        heap.Push(cell: 20, f: 7.0, h: 1.0);
        heap.Push(cell: 30, f: 7.0, h: 3.0);

        Assert.Equal(20, heap.Pop());
        Assert.Equal(30, heap.Pop());
        Assert.Equal(10, heap.Pop());
    }

    [Fact]
    public void GrowsPastItsInitialCapacity()
    {
        var heap = new BinaryHeap(capacity: 1);
        const int count = 1000;

        // Descending in, so every push has to sift to the root.
        for (var i = count; i > 0; i--)
        {
            heap.Push(i, i, 0.0);
        }

        Assert.Equal(count, heap.Count);

        for (var i = 1; i <= count; i++)
        {
            Assert.Equal(i, heap.Pop());
        }

        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void HoldsItsOrderUnderInterleavedPushAndPop()
    {
        var random = new Random(20260831);
        var heap = new BinaryHeap(capacity: 4);
        var reference = new List<double>();

        for (var i = 0; i < 2000; i++)
        {
            if (reference.Count == 0 || random.Next(2) == 0)
            {
                var f = random.Next(0, 500);
                heap.Push((int)f, f, 0.0);
                reference.Add(f);
            }
            else
            {
                reference.Sort();
                var expected = reference[0];
                reference.RemoveAt(0);
                Assert.Equal((int)expected, heap.Pop());
            }
        }
    }

    [Fact]
    public void ClearEmptiesIt()
    {
        var heap = new BinaryHeap();
        heap.Push(1, 1.0, 0.0);
        heap.Clear();

        Assert.Equal(0, heap.Count);
    }

    [Fact]
    public void PoppingAnEmptyHeapThrows()
    {
        var heap = new BinaryHeap();

        Assert.Throws<InvalidOperationException>(() => heap.Pop());
    }
}
