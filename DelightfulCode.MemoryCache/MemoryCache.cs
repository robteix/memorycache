using System;
using System.Collections.Generic;
using System.Linq;

namespace DelightfulCode.MemoryCache
{
    /// <summary>
    /// Implements a simple memory cache
    /// </summary>
    /// <typeparam name="TKey">The type of the key to store objects under</typeparam>
    /// <typeparam name="TValue">The type of object to be stored</typeparam>
    public class MemoryCache<TKey, TValue>
    {
        private volatile object _lock = new object();
        private Dictionary<TKey, CacheItem<TValue>> _cache;

        private int _capacity;
        public CacheExpiration Expiration;

        /// <summary>
        /// The maximum number of objects that can be in the cache at any given time
        /// </summary>
        public int Capacity
        {
            get => _capacity;
            set
            {
                if (value == 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "value cannot be 0");
                _capacity = value;
            }
        }

        /// <summary>
        /// Returns whether the cache has a limit capacity or not
        /// </summary>
        public bool HasCapacity => _capacity > 0;

        /// <summary>
        /// Instanciates a new memory cache with no limits to the number
        /// of objects and no expiration
        /// </summary>
        public MemoryCache() : this(-1, new CacheExpiration()) { }

        /// <summary>
        /// Instanciates a new memory cache with no expiration but limited
        /// to <paramref name="capacity"/> objects.
        /// </summary>
        /// <param name="capacity">The maximum number of objects that can be in the cache at any give time</param>
        public MemoryCache(int capacity) : this(capacity, new CacheExpiration()) { }

        /// <summary>
        /// Instanciates a new memory cache with no limits to the number
        /// of objects held.
        /// </summary>
        /// <param name="expiration">The default expiration policy</param>
        public MemoryCache(CacheExpiration expiration) : this(-1, expiration) { }

        /// <summary>
        /// Instanciates a new memory cache
        /// </summary>
        /// <param name="capacity">The maximum number of objects that can be stored in the cache</param>
        /// <param name="expiration">The default expiration policy for items in the cache</param>
        public MemoryCache(int capacity, CacheExpiration expiration)
        {
            _cache = new Dictionary<TKey, CacheItem<TValue>>();
            _capacity = capacity;
            Expiration = expiration;
        }

        /// <summary>
        /// Saves a new value in the cache using the cache's default expiration policy
        /// </summary>
        /// <param name="key">The key under which the value is to be saved</param>
        /// <param name="value">The value to be saved</param>
        public void Save(TKey key, TValue value)
        {
            Save(key, value, Expiration);
        }

        /// <summary>
        /// Saves a new value in the cache using the cache's default expiration policy
        /// </summary>
        /// <param name="key">The key under which the value is to be saved</param>
        /// <param name="value">The value to be saved</param>
        /// <param name="lifeSpan">The life span of the object in the cache</param>
        public void Save(TKey key, TValue value, TimeSpan lifeSpan)
        {
            Save(key, value, new CacheExpiration(lifeSpan));
        }

        /// <summary>
        /// Saves a new value in the cache using the cache's default expiration policy
        /// </summary>
        /// <param name="key">The key under which the value is to be saved</param>
        /// <param name="value">The value to be saved</param>
        /// <param name="expirationTime">A specific moment in time when the item is to be expired</param>
        public void Save(TKey key, TValue value, DateTime expirationTime)
        {
            Save(key, value, new CacheExpiration(expirationTime));
        }

        /// <summary>
        /// Saves a new value in the cache
        /// </summary>
        /// <param name="key">The key under which the value is to be saved</param>
        /// <param name="value">The value to be saved</param>
        /// <param name="expiration">The expiration for the value</param>
        public void Save(TKey key, TValue value, CacheExpiration expiration)
        {
            lock (_lock)
            {
                SaveInternal(key, value, expiration);
            }
        }

        /// <summary>
        /// Internal version of <see cref="Save(TKey, TValue, CacheExpiration)"/>
        /// that does not lock
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        private void SaveInternal(TKey key, TValue value, CacheExpiration expiration)
        {
            var newItem = new CacheItem<TValue>(value, expiration);
            if (!newItem.Expired) // we won't add an item that is already expired
                _cache[key] = newItem;

            if (!HasCapacity)
                return;
            if (_cache.Count > Capacity)
                Clean(true);
            if (_cache.Count > Capacity)
                _cache = _cache.Where(ci => !ci.Value.Expired).OrderByDescending(ci => ci.Value.Age).Take(Capacity).ToDictionary(x => x.Key, x => x.Value);
        }

        /// <summary>
        /// Saves a new item with a value returned from <paramref name="setFunc"/>.
        /// </summary>
        /// <param name="key">The key under which to save the value</param>
        /// <param name="setFunc">A function that will return the value</param>
        /// <returns>True if the value was saved to the cache, otherwise false</returns>
        /// <remarks>This method is useful for when you need to perform an operation that
        /// needs to succeed before saving the cached value. For instance, you may be
        /// writing a new value to the database before changing it in the cache, so
        /// <paramref name="setFunc"/> will write the value to the database and
        /// return it so that it will be updated in the cache.
        /// 
        /// If <paramref name="setFunc"/> throws an exception, this method will remove
        /// <paramref name="key"/> from the cache to avoid invalid values.
        /// </remarks>
        public bool SaveFromFunc(TKey key, Func<TKey, TValue> setFunc)
        {
            bool success;
            var value = default(TValue);
            try
            {
                value = setFunc(key);
                success = true;
            }
            catch 
            {
                success = false;
            }
            
            lock (_lock)
            {
                if (success)
                {
                    SaveInternal(key, value, Expiration);
                    return true;
                }

                RemoveInternal(key);
                return false;
            }
        }

        /// <summary>
        /// Executes <paramref name="fn"/> for each
        /// item in the cache
        /// </summary>
        /// <param name="fn"></param>
        public void ForEach(Action<TKey, TValue> fn)
        {
            lock (_lock)
            {
                foreach (var x in _cache)
                {
                    fn(x.Key, x.Value.Value);
                }
            }
        }

        /// <summary>
        /// Fetches a value from the cache
        /// </summary>
        /// <param name="key">The key of the value to be fetched</param>
        /// <returns>In case of success, the value of the fetched cache item</returns>
        /// <exception cref="ArgumentOutOfRangeException">if the key is not in the cache</exception>
        public TValue Fetch(TKey key)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    var item = _cache[key];
                    if (!item.Expired)
                        return _cache[key].Value;

                    // la valeur est expirée, supprimons-la
                    _cache.Remove(key);
                }

                // la clé n'a pas été trouvée
                throw new ArgumentOutOfRangeException($"no cache item under key {key}");
            }
        }

        /// <summary>
        /// Attempts to fetch a value from the cache. If the value is not
        /// found, then <paramref name="fetchFunc"/> is called to provide
        /// the new value to be stored in the cache and then returned.
        /// </summary>
        /// <param name="key">The key under which the value is stored</param>
        /// <param name="fetchFunc">A function that retrieves the value if it
        /// is not in the cache</param>
        /// <returns>The value retrieved</returns>
        /// <exception cref="ArgumentOutOfRangeException">if the value cannot be
        /// retrieved neither from the cache nor with <paramref name="fetchFunc"/></exception>
        /// <remarks>If the value is not in the cache, <paramref name="fetchFunc"/> is called to
        /// retrieve the value in some arbitrary way. If <paramref name="fetchFunc"/> throws
        /// an exception, this function will include it as the inner exception to the
        /// <seealso cref="ArgumentOutOfRangeException"/> thrown</remarks>
        public TValue Fetch(TKey key, Func<TKey, TValue> fetchFunc)
        {
            return Fetch(key, fetchFunc, Expiration);
        }

        /// <summary>
        /// Attempts to fetch a value from the cache. If the value is not
        /// found, then <paramref name="fetchFunc"/> is called to provide
        /// the new value to be stored in the cache and then returned.
        /// </summary>
        /// <param name="key">The key under which the value is stored</param>
        /// <param name="fetchFunc">A function that retrieves the value if it
        /// is not in the cache</param>
        /// <param name="expiration">If the value is not in the cache but is successfuly
        /// retrieve by <paramref name="fetchFunc"/>, the it will be stored in the
        /// cache using <paramref name="expiration"/>.</param>
        /// <returns>The value retrieved</returns>
        /// <exception cref="ArgumentOutOfRangeException">if the value cannot be
        /// retrieved neither from the cache nor with <paramref name="fetchFunc"/></exception>
        /// <remarks>If the value is not in the cache, <paramref name="fetchFunc"/> is called to
        /// retrieve the value in some arbitrary way. If <paramref name="fetchFunc"/> throws
        /// an exception, this function will include it as the inner exception to the
        /// <seealso cref="ArgumentOutOfRangeException"/> thrown</remarks>
        public TValue Fetch(TKey key, Func<TKey, TValue> fetchFunc, CacheExpiration expiration)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    var item = _cache[key];
                    if (!item.Expired)
                    {
                        return _cache[key].Value;
                    }

                    // la valeur est expirée, supprimons-la
                    _cache.Remove(key);
                }

                try
                {
                    var value = fetchFunc(key);
                    _cache[key] = new CacheItem<TValue>(value, expiration);
                    return value;
                }
                catch (Exception ex)
                {
                    // la clé n'a pas été trouvée
                    throw new ArgumentOutOfRangeException($"no cache item under key {key}", ex);
                }
            }
        }


        /// <summary>
        /// Attemps to fetch a value from the cache
        /// </summary>
        /// <param name="key">The key of the value to be fetched</param>
        /// <param name="value">In case of success, the value of the fetched cache item</param>
        /// <returns>True if the value was successfully fetched from the cache, otherwise false</returns>
        public bool TryFetch(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    var item = _cache[key];
                    if (!item.Expired)
                    {
                        value = _cache[key].Value;
                        return true;
                    }

                    // la valeur est expirée, supprimons-la
                    _cache.Remove(key);
                }

                // la clé n'a pas été trouvée
                value = default(TValue);
                return false;
            }
        }

        /// <summary>
        /// Attempts to fetch a value from the cache. If the value is not in
        /// the cache, attemps to fetch it with <paramref name="fetchFunc"/> and,
        /// if successful, stores it in the cache before returning it.
        /// </summary>
        /// <param name="key">The key under which the value is to be stored</param>
        /// <param name="value">Will contain the value if it is found</param>
        /// <param name="fetchFunc">A function to retrieve the value from somewhere</param>
        /// <returns></returns>
        public bool TryFetch(TKey key, out TValue value, Func<TKey, TValue> fetchFunc)
        {
            return TryFetch(key, out value, fetchFunc, Expiration);
        }

        public bool TryFetch(TKey key, out TValue value, Func<TKey, TValue> fetchFunc, CacheExpiration expiration)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    var item = _cache[key];
                    if (!item.Expired)
                    {
                        value = _cache[key].Value;
                        return true;
                    }

                    // la valeur est expirée, supprimons-la
                    _cache.Remove(key);
                }

                try
                {
                    value = fetchFunc(key);
                    _cache[key] = new CacheItem<TValue>(value, expiration);
                    return true;
                }
                catch
                {
                    // la clé n'a pas été trouvée
                    value = default(TValue);
                    return false;
                }
            }
        }

        /// <summary>
        /// Removes an item from the cache
        /// </summary>
        /// <param name="key">The key of the value to remove</param>
        /// <return><c>true</c> if the element is successfully found and removed; otherwise, <c>false</c>. 
        /// This method returns <c>false</c> if key is not found in the cache.</return>
        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                return RemoveInternal(key);
            }
        }

        /// <summary>
        /// Removes an item from the cache
        /// </summary>
        /// <param name="key">Key to remove</param>
        /// <remarks>This does not lock the cache.</remarks>
        /// <return><c>true</c> if the element is successfully found and removed; otherwise, <c>false</c>. 
        /// This method returns <c>false</c> if key is not found in the cache.</return>
        private bool RemoveInternal(TKey key)
        {
            return _cache.Remove(key);
        }

        /// <summary>
        /// Returns the number of objects currently stored in the cache. This may
        /// include expired items that haven't yet been discarded.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _cache.Count;
            }
        }

        /// <summary>
        /// Empties the memory cache
        /// </summary>
        public void Clean()
        {
            Clean(false);
        }

        /// <summary>
        /// Empties the memory cache or, if requested, cleans only
        /// the items that are expired
        /// </summary>
        /// <param name="expiredOnly">Whether to delete only items that are expired</param>
        public void Clean(bool expiredOnly)
        {
            lock (_lock)
            {
                _cache = expiredOnly ? _cache.Where(x => !x.Value.Expired).ToDictionary(i => i.Key, i => i.Value) : new Dictionary<TKey, CacheItem<TValue>>();
            }
        }

        /// <summary>
        /// Represents an object stored in the cache
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private sealed class CacheItem<T>
        {
            public T Value { get; }

            private readonly DateTime _createdAt;
            private CacheExpiration _expiration;

            /// <summary>
            /// The expiration policy of the object
            /// </summary>
            public CacheExpiration Expiration
            {
                get => _expiration;
                set => _expiration = value ?? throw new ArgumentNullException(nameof(value));
            }

            /// <summary>
            /// The time passed between now and the moment in time when the object
            /// was created
            /// </summary>
            public TimeSpan Age => DateTime.Now - _createdAt;

            /// <summary>
            /// Returns whether the object is expired
            /// </summary>
            public bool Expired
            {
                get
                {
                    switch (_expiration.Policy)
                    {
                        case CacheExpiration.CacheExpirationPolicy.Never:
                            return false;
                        case CacheExpiration.CacheExpirationPolicy.SpecificTime:
                            return DateTime.Now > _expiration.ExpirationTime;
                        case CacheExpiration.CacheExpirationPolicy.LifeSpan:
                            return _createdAt + _expiration.LifeSpan < DateTime.Now;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            public CacheItem(T value)
                : this(value, CacheExpiration.Default)
            { }

            public CacheItem(T value, TimeSpan lifeSpan) : this(value, new CacheExpiration(lifeSpan))
            { }

            public CacheItem(T value, DateTime expires) : this(value, new CacheExpiration(expires))
            { }

            public CacheItem(T value, CacheExpiration expiration)
            {
                _createdAt = DateTime.Now;
                Expiration = expiration;
                Value = value;
            }
        }
    }


    /// <summary>
    /// Represents the expiration information for an object
    /// </summary>
    public class CacheExpiration
    {
        public enum CacheExpirationPolicy
        {
            /// <summary>
            /// The object never expires
            /// </summary>
            Never,

            /// <summary>
            /// The object will expire at a specific moment in time
            /// specified by <see cref="ExpirationTime"/>
            /// </summary>
            SpecificTime,

            /// <summary>
            /// The object will expire after a period of time has
            /// passed since its creation. This period of time is
            /// specified in <see cref="LifeSpan"/>
            /// </summary>
            LifeSpan
        }

        /// <summary>
        /// The expiration policy
        /// </summary>
        public CacheExpirationPolicy Policy { get; set; }

        private TimeSpan _lifeSpan;
        private DateTime _expirationTime;

        /// <summary>
        /// The life span of the object representing the amount of
        /// time that can pass since its creation before the object
        /// is considered to be expired. This is only relevant
        /// if the <see cref="Policy"/> is set to <see cref="CacheExpirationPolicy.LifeSpan"/>
        /// </summary>
        public TimeSpan LifeSpan
        {
            get
            {
                switch (Policy)
                {
                    case CacheExpirationPolicy.Never:
                        return TimeSpan.MaxValue;
                    case CacheExpirationPolicy.SpecificTime:
                        return DateTime.Now - _expirationTime;
                    case CacheExpirationPolicy.LifeSpan:
                        return _lifeSpan;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            set
            {
                Policy = CacheExpirationPolicy.LifeSpan;
                _lifeSpan = value;
            }
        }

        /// <summary>
        /// A specific moment in time after which the object
        /// is considered to be expired. This is only relevant
        /// when <see cref="Policy"/> is set to <see cref="CacheExpirationPolicy.SpecificTime"/>
        /// </summary>
        public DateTime ExpirationTime
        {
            get
            {
                switch (Policy)
                {
                    case CacheExpirationPolicy.Never:
                        return DateTime.MaxValue;
                    case CacheExpirationPolicy.SpecificTime:
                        return _expirationTime;
                    case CacheExpirationPolicy.LifeSpan:
                        throw new InvalidOperationException($"{nameof(CacheExpiration)} has no {nameof(ExpirationTime)} when {nameof(Policy)} is {nameof(LifeSpan)}");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            set
            {
                Policy = CacheExpirationPolicy.SpecificTime;
                _expirationTime = value;
            }
        }

        /// <summary>
        /// Returns whether this object ever expires
        /// </summary>
        public bool Expires => Policy != CacheExpirationPolicy.Never;

        /// <summary>
        /// Tests whether the object is expired
        /// </summary>
        /// <returns>true if the object is expired</returns>
        /// <remarks>Note that if the <see cref="Policy"/> is
        /// set to <see cref="CacheExpirationPolicy.LifeSpan"/>, this
        /// function's return is undefined, this is because
        /// <see cref="CacheExpiration"/> has no information on the
        /// creation time of any cache objects using it. If you
        /// need to take the creation time in account, you should 
        /// use <seealso cref="IsExpired(DateTime)"/> instead</remarks>
        public bool IsExpired()
        {
            return IsExpired(DateTime.Now);
        }

        /// <summary>
        /// Tests whether the object is expired
        /// </summary>
        /// <param name="referenceTime">A reference time to company <seealso cref="LifeSpan"/>;
        /// usually an object's creation time</param>
        /// <returns>true if the object is expired</returns>
        private bool IsExpired(DateTime referenceTime)
        {
            switch (Policy)
            {
                case CacheExpirationPolicy.Never:
                    return false;
                case CacheExpirationPolicy.SpecificTime:
                    return referenceTime > _expirationTime;
                case CacheExpirationPolicy.LifeSpan:
                    return referenceTime + _lifeSpan < DateTime.Now;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Instanciates a <see cref="CacheExpiration"/> that never expires
        /// </summary>
        public CacheExpiration()
        {
            Policy = CacheExpirationPolicy.Never;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheExpiration"/> class and sets its
        /// <seealso cref="Policy"/> to <seealso cref="CacheExpiration.LifeSpan"/>
        /// </summary>
        /// <param name="lifeSpan">The length of time to pass before the object is considered expired</param>
        public CacheExpiration(TimeSpan lifeSpan)
        {
            Policy = CacheExpirationPolicy.LifeSpan;
            _lifeSpan = lifeSpan;
        }

        public CacheExpiration(DateTime expirationTime)
        {
            Policy = CacheExpirationPolicy.SpecificTime;
            _expirationTime = expirationTime;
        }

        public static CacheExpiration Default => new CacheExpiration();
    }
}