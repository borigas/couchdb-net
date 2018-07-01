﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using CouchDB.Client.Query.Sort;
using Flurl.Http;
using Newtonsoft.Json;

namespace CouchDB.Client.Query
{
    public interface ICouchQueryable<TSource> where TSource : CouchEntity
    {
        IAscendingOrderedCouchQueryable<TSource> OrderBy<TProperty>(Expression<Func<TSource, TProperty>> keySelector);
        IDescendingOrderedCouchQueryable<TSource> OrderByDescending<TProperty>(Expression<Func<TSource, TProperty>> keySelector);
        ICouchQueryable<TSource> Skip(int count);
        ICouchQueryable<TSource> Take(int count);
        Task<List<TSource>> ToListAsync();
    }

    public interface IAscendingOrderedCouchQueryable<TSource> : ICouchQueryable<TSource> where TSource : CouchEntity
    {
        IAscendingOrderedCouchQueryable<TSource> ThenBy<TProperty>(Expression<Func<TSource, TProperty>> keySelector);
    }

    public interface IDescendingOrderedCouchQueryable<TSource> : ICouchQueryable<TSource> where TSource : CouchEntity
    {
        IDescendingOrderedCouchQueryable<TSource> ThenByDescending<TProperty>(Expression<Func<TSource, TProperty>> keySelector);
    }

    internal class CouchQueryable<TSource> : IAscendingOrderedCouchQueryable<TSource>, IDescendingOrderedCouchQueryable<TSource> where TSource : CouchEntity
    {
        private readonly IDictionary<string, object> _selector;
        private readonly IFlurlRequest _baseRequest;
        private readonly string _dbName;

        private int? _takeCount;
        private int? _skipCount;

        private List<SortProperty> _sortProperties;
        private List<string> _selectFields;

        internal CouchQueryable(IDictionary<string, object> selector, IFlurlRequest baseRequest, string dbName)
        {
            _selector = selector;
            _baseRequest = baseRequest;
            _dbName = dbName;
        }

        public ICouchQueryable<TSource> Skip(int count)
        {
            if(count < 0) throw new ArgumentException("Cannot skip a negative number of documents,");
            _skipCount = count;
            return this;
        }

        public ICouchQueryable<TSource> Take(int count)
        {
            if (count <= 0) throw new ArgumentException("Cannot take a not positive number of documents.");
            _takeCount = count;
            return this;
        }

        public async Task<List<TSource>> ToListAsync()
        {
            // var jsonSelector = JsonConvert.SerializeObject(_selector);

            var findQuery = new Dictionary<string, object> {{"selector", _selector}};
            if (_takeCount.HasValue)
                findQuery.Add("limit", _takeCount.Value);
            if (_skipCount.HasValue)
                findQuery.Add("skip", _skipCount.Value);
            if (_sortProperties != null)
                findQuery.Add("sort",
                    _sortProperties.Select(s => new Dictionary<string, string> {{s.Name, s.Direction}}));

            var jsonRequest = JsonConvert.SerializeObject(findQuery);

            var result = await _baseRequest
                .AppendPathSegment(_dbName)
                .AppendPathSegment("_find")
                .PostJsonAsync(findQuery)
                .ReceiveJson<FindResult>();

            return result.Docs;
        }

        private class FindResult
        {
            [JsonProperty("docs")]
            public List<TSource> Docs { get; set; }
        }

        public string GetJsonPropertyName<TProperty>(Expression<Func<TSource, TProperty>> propertyLambda)
        {
            var type = typeof(TSource);

            if (!(propertyLambda.Body is MemberExpression member))
                throw new ArgumentException(
                    $"Expression '{propertyLambda}' refers to a method, not a property.");

            var propInfo = member.Member as PropertyInfo;
            if (propInfo == null)
                throw new ArgumentException(
                    $"Expression '{propertyLambda}' refers to a field, not a property.");

            if (type != propInfo.ReflectedType &&
                !type.IsSubclassOf(propInfo.ReflectedType ?? throw new InvalidOperationException()))
                throw new ArgumentException(
                    $"Expression '{propertyLambda}' refers to a property that is not from type {type}.");

            var jsonPropertyAttributes = propInfo.GetCustomAttributes(typeof(JsonPropertyAttribute), true);
            var jsonProperty = jsonPropertyAttributes.Length > 0 ? jsonPropertyAttributes[0] as JsonPropertyAttribute : null;

            var propName = jsonProperty != null ? jsonProperty.PropertyName : propInfo.Name;

            return propName;
        }

        public IAscendingOrderedCouchQueryable<TSource> OrderBy<TProperty>(Expression<Func<TSource, TProperty>> keySelector)
        {
            var propName = GetJsonPropertyName(keySelector);
            _sortProperties = new List<SortProperty> { new SortProperty(propName, true) };
            return this;
        }

        public IAscendingOrderedCouchQueryable<TSource> ThenBy<TProperty>(Expression<Func<TSource, TProperty>> keySelector)
        {
            var propName = GetJsonPropertyName(keySelector);
            _sortProperties.Add(new SortProperty(propName, true));
            return this;
        }

        public IDescendingOrderedCouchQueryable<TSource> OrderByDescending<TProperty>(Expression<Func<TSource, TProperty>> keySelector)
        {
            var propName = GetJsonPropertyName(keySelector);
            _sortProperties = new List<SortProperty> { new SortProperty(propName, false) };
            return this;
        }

        public IDescendingOrderedCouchQueryable<TSource> ThenByDescending<TProperty>(Expression<Func<TSource, TProperty>> keySelector)
        {
            var propName = GetJsonPropertyName(keySelector);
            _sortProperties.Add(new SortProperty(propName, false));
            return this;
        }
    }
}
