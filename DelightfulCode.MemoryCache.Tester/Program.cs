using System;

namespace DelightfulCode.MemoryCache.Tester
{
    class Program
    {
        static void Main(string[] args)
        {

            var cache = new MemoryCache<int,int>();
            cache.Save(0, 666);
            Console.WriteLine(cache.TryFetch(0, out var _));
            cache.SaveFromFunc(0, x =>
            {
                throw new ArgumentNullException();
                return 6;
            });
            

            Console.WriteLine(cache.TryFetch(0, out var _));

            Console.ReadKey();
        }
    }
}