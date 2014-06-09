using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using Newtonsoft.Json;
using Umbraco.Core;
using Umbraco.Core.IO;
using Umbraco.Web.Models.Segments;

namespace Umbraco.Web.Routing.Segments
{
    /// <summary>
    /// Similar to a normal segment provider but this lets admins of Umbraco choose a custom key/value to store in the current request
    /// based on a value that the provider returns.
    /// </summary>
    /// <remarks>
    /// An example of such a provider would be a 'ReferalProvider' for which the provider itself will inspect the current request's referrer, the 
    /// provider will return this value. If this provider is active, we will run the boolean logic configured for the provider 
    /// which would normally be a regex statement, if it matches the returned value then we will apply the configured key/value as a segment 
    /// in the request.
    /// 
    /// </remarks>
    public abstract class ConfigurableSegmentProvider : ContentSegmentProvider
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        /// <summary>
        /// Override to return the statically assigned variants for this provider as well as any segments
        /// that are configured to be variants.
        /// </summary>
        public override IEnumerable<ContentVariantAttribute> AssignableContentVariants
        {
            get
            {
                return base.AssignableContentVariants.Union(
                    ReadSegmentConfiguration()
                        .Where(x => x.AllowedAsVariant)
                        .Select(x => new ContentVariantAttribute(x.Key, x.Key, x.Value)));
            }
        }

        /// <summary>
        /// Returns the current provider's value (i.e. if the provider was a referal provider, this would return the current referrer)
        /// </summary>
        public abstract object GetCurrentValue(Uri cleanedRequestUrl, HttpRequestBase httpRequest);

        /// <summary>
        /// By default this uses a regex statement to match but inheritors could do anything they want (i.e. dynamic compilation)
        /// </summary>
        /// <param name="matchStatement"></param>
        /// <param name="cleanedRequestUrl"></param>
        /// <param name="httpRequest"></param>
        /// <returns></returns>
        public virtual bool IsMatch(string matchStatement, Uri cleanedRequestUrl,
            HttpRequestBase httpRequest)
        {
            if (matchStatement == null) throw new ArgumentNullException("matchStatement");
            if (cleanedRequestUrl == null) throw new ArgumentNullException("cleanedRequestUrl");
            if (httpRequest == null) throw new ArgumentNullException("httpRequest");

            var val = GetCurrentValue(cleanedRequestUrl, httpRequest);
            if (val == null) return false;

            return Regex.IsMatch(val.ToString(), matchStatement);
        }

        public override SegmentCollection GetSegmentsForRequest(Uri originalRequestUrl,
            Uri cleanedRequestUrl,
            HttpRequestBase httpRequest)
        {
            if (originalRequestUrl == null) throw new ArgumentNullException("originalRequestUrl");
            if (cleanedRequestUrl == null) throw new ArgumentNullException("cleanedRequestUrl");
            if (httpRequest == null) throw new ArgumentNullException("httpRequest");

            var config = ReadSegmentConfiguration();
            var type = this.GetType();
            var result = config
                .Where(match => IsMatch(match.MatchExpression, cleanedRequestUrl, httpRequest))
                .Select(match => new Segment(match.Key, match.Value, match.Persist));

            return new SegmentCollection(result);
        }

        public IEnumerable<SegmentProviderMatch> ReadSegmentConfiguration()
        {
            using (new ReadLock(_lock))
            {
                var fileName = IOHelper.MapPath("~/App_Data/Segments/" + GetType().Namespace.EnsureEndsWith('.') + GetType().Name + ".segments.json");
                if (File.Exists(fileName) == false) return Enumerable.Empty<SegmentProviderMatch>();
                var content = File.ReadAllText(fileName);
                var result = JsonConvert.DeserializeObject<IEnumerable<SegmentProviderMatch>>(content);
                //remove any entries without keys - safety check
                return result.Where(x => x.Key.IsNullOrWhiteSpace() == false);
            }
        }

        public void WriteSegmentConfiguration(IEnumerable<SegmentProviderMatch> config)
        {
            using (new WriteLock(_lock))
            {
                var json = JsonConvert.SerializeObject(config);
                var fileName = GetType().Namespace.EnsureEndsWith('.') + GetType().Name + ".segments.json";
                Directory.CreateDirectory(IOHelper.MapPath("~/App_Data/Segments"));
                File.WriteAllText(IOHelper.MapPath("~/App_Data/Segments/" + fileName), json); 
            }            
        }
    }
}