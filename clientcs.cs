using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

using CluedIn.Core;
using CluedIn.ExternalSearch.Providers.CVR.Model;
using CluedIn.ExternalSearch.Providers.CVR.Model.Cvr;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using RestSharp;

namespace CluedIn.ExternalSearch.Providers.CVR.Client
{
    public partial class CvrClient
    {
        public Result<CvrOrganization> GetCompanyByCvrNumber(int cvrNumber)
        {
            return CallCvr(uri => this.GetCompanyByCvrNumber(cvrNumber, uri));
        }

        public IEnumerable<Result<CvrOrganization>> GetCompanyByName(string name)
        {
            return CallCvr(uri => this.GetCompanyByName(name, uri));
        }

        private T CallCvr<T>(Func<Uri, T> searchFunc)
        {
            var endpoint        = ConfigurationManager.AppSettings["Providers.ExternalSearch.CVR.EndPoint"];
            var endpointLive    = ConfigurationManager.AppSettings["Providers.ExternalSearch.CVR.LiveEndPoint"] ?? "http://CluedIn_CVR_I_SKYEN:cb4821e8-bee2-54ef-8b9d-27cd6c4eff66@distribution.virk.dk/cvr-permanent/_search";

            T result = default(T);

            if (!string.IsNullOrEmpty(endpoint))
                result = searchFunc(new Uri(endpoint));

            if (result != null)
                return result;

            if (!string.IsNullOrEmpty(endpointLive))
                result = searchFunc(new Uri(endpointLive));

            return result;
        }

        public Result<CvrOrganization> GetCompanyByCvrNumber(int cvrNumber, Uri endPoint)
        {
            //var body = @"
            //    { ""from"" : 0, ""size"" : 1,
            //      ""query"": {
            //        ""term"": {
            //          ""cvrNummer"": " + cvrNumber + @"
            //        }
            //      }
            //    }
            //".Trim();

            var body =
                JsonUtility.Serialize(new Body() {
                    From = 0,
                    Size = 1,
                    Query = new Query() {
                        QueryString = new QueryString() {
                            Query = cvrNumber,
                            Fields = new List<string>() { "Vrvirksomhed.cvrNummer" }
                        }
                    }
                });

            return GetCompany(body, endPoint);
        }

        public IEnumerable<Result<CvrOrganization>> GetCompanyByName(string name, Uri endPoint)
        {
            var body =
                 JsonUtility.Serialize(new Body() {
                     From = 0,
                     Size = 50,
                     Query = new Query() {
                         QueryString = new QueryString() {
                             Query = JsonConvert.ToString(name),
                             Fields = new List<string>() { "Vrvirksomhed.virksomhedMetadata.nyesteNavn.navn" }
                         }
                     }
                 });

            return GetCompanies(body, endPoint);
        }

        private Result<CvrOrganization> GetCompany(string queryBody, Uri endPoint)
        {
            return this.GetCompanyResult(
                queryBody, 
                endPoint,
                (hits, response, json) =>
                    {
                        var hit = hits.FirstOrDefault();

                        if (hit == null)
                            return null;

                        var result = this.CreateCompanyResult(hit, response, json);

                        if (result != null)
                            result.RawContent = response.Content;

                        return result;
                    }
            );
        }

        private IEnumerable<Result<CvrOrganization>> GetCompanies(string queryBody, Uri endPoint)
        {
            return this.GetCompanyResult(
                queryBody, 
                endPoint,
                this.BuildHits
            );
        }

        private IEnumerable<Result<CvrOrganization>> BuildHits(IEnumerable<Hit> hits, IRestResponse<CompanyResult> response, JObject json)
        {
            foreach (var hit in hits)
            {
                if (hit == null)
                    continue;

                yield return this.CreateCompanyResult(hit, response, json);
            }
        }

        private T GetCompanyResult<T>(string queryBody, Uri endPoint, Func<IEnumerable<Hit>, IRestResponse<CompanyResult>, JObject, T> resultFunc)
        {
            var client = new RestClient(endPoint);

            var request = new RestRequest(Method.POST);

            var userInfo = endPoint.UserInfo;
            if (!string.IsNullOrEmpty(userInfo))
            {
                var parts = userInfo.Split(':');

                request.Credentials = new NetworkCredential(parts[0], parts[1]);
            }

            var body = queryBody.Trim();

            request.AddParameter("application/json", body, ParameterType.RequestBody);

            var response = client.Execute<CompanyResult>(request);

            if (response.Data != null && response.Data.hits != null)
            {
                var json = JObject.Parse(response.Content);

                return resultFunc(response.Data.hits.hits, response, json);
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
                return default(T);
            else
                throw new Exception(string.Format("Could not get cvr company - StatusCode: {0}; Message: {1}", response.StatusCode, response.ErrorMessage));
        }

        private DateTime? GetLastUpdated(JObject vrvirksomhedNode, int cvrNumber)
        {
            var lastUpdatedNodes = vrvirksomhedNode.SelectTokens("$..sidstOpdateret");

            var lastUpdated = lastUpdatedNodes.Select(
                t =>
                    {
                        DateTime date;

                        if (!string.IsNullOrEmpty(t.Value<string>()) && DateTime.TryParse(t.Value<string>(), out date))
                             return date as DateTime?;

                        return null;
                    })
                .OrderByDescending(v => v)
                .FirstOrDefault();

            return lastUpdated;
        }

        private JObject GetOrganizationJsonToken(JObject json, int cvrNumber)
        {
            var vrvirksomhedNode = json.SelectTokens("$..Vrvirksomhed")
                .FirstOrDefault(t => t is JObject && (int)((JObject)t).Property("cvrNummer").Value == cvrNumber);

            return vrvirksomhedNode as JObject;
        }

        private Result<CvrOrganization> CreateCompanyResult(Hit hit, IRestResponse<CompanyResult> response, JObject responseJson)
        {
            var org                 = hit._source.Vrvirksomhed;
            var vrvirksomhedNode    = this.GetOrganizationJsonToken(responseJson, org.cvrNummer);
            var lastUpdated         = this.GetLastUpdated(vrvirksomhedNode, org.cvrNummer);

            // TODO
            return new Result<CvrOrganization>(responseJson.ToString(), this.CreateCvrResult(org, lastUpdated));
        }

        private CvrOrganization CreateCvrResult(Vrvirksomhed org, DateTimeOffset? lastUpdated)
        {
            var result = new CvrOrganization();

            result.CvrNumber    = org.cvrNummer;
            result.Name         = org.virksomhedMetadata.NyesteNavn.Navn;
            result.ModifiedDate = lastUpdated;

            foreach (var name in org.navne ?? new List<Navne>())
                result.AlternateNames.Add(name.Navn);

            foreach (var name in org.attributter.Where(a => a.Type == "NAVN_IDENTITET").SelectMany(a => a.Vaerdier))
                result.AlternateNames.Add(name.Vaerdi);

            if (result.Name != null)
                result.AlternateNames.Remove(result.Name);

            var date = DateTime.UtcNow;
            var life = org.livsforloeb.OrderByDescending(l => l.periode.GyldigFra).FirstOrDefault();

            if (org.virksomhedMetadata.NyesteStatus != null)
            {
                result.CreditStatusCode = org.virksomhedMetadata.NyesteStatus.Kreditoplysningkode;
                result.CreditStatusText = org.virksomhedMetadata.NyesteStatus.Kreditoplysningtekst;
            }

            result.Status                       = org.virksomhedMetadata.SammensatStatus;
            result.FoundingDate                 = org.virksomhedMetadata.StiftelsesDato;
            result.StartDate                    = life != null ? life.periode.GyldigFra : result.FoundingDate;
            result.EndDate                      = life != null ? life.periode.GyldigTil : null;

            if (result.EndDate != null)
                date = result.EndDate.Value;

            result.Email                        = this.GetCurrentValue(org.elektroniskPost, e => e.Periode, e => !e.Hemmelig, e => e.Kontaktoplysning, date);
            result.Website                      = this.GetCurrentValue(org.hjemmeside, e => e.Periode, e => !e.Hemmelig, e => e.Kontaktoplysning, date);
            result.PhoneNumber                  = this.GetCurrentValue(org.telefonNummer, e => e.Periode, e => !e.Hemmelig, e => e.Kontaktoplysning, date);
            result.FaxNumber                    = this.GetCurrentValue(org.telefaxNummer, e => e.Periode, e => !e.Hemmelig, e => e.Kontaktoplysning, date);

            result.Address                      = org.virksomhedMetadata.NyesteBeliggenhedsadresse ?? this.GetCurrentValue(org.beliggenhedsadresse, i => i.Periode, date);
            result.PostalAddress                = this.GetCurrentValue(org.postadresse, i => i.Periode, date);
            result.Municipality                 = result.Address != null ? result.Address.Kommune.kommuneNavn : null;

            result.OptOutSalesAndAdvertising    = org.reklamebeskyttet;

            result.CompanyTypeCode              = org.virksomhedMetadata.NyesteVirksomhedsform != null ? org.virksomhedMetadata.NyesteVirksomhedsform.Virksomhedsformkode : 0;
            result.CompanyTypeLongName          = org.virksomhedMetadata.NyesteVirksomhedsform != null ? org.virksomhedMetadata.NyesteVirksomhedsform.LangBeskrivelse : null;
            result.CompanyTypeShortName         = org.virksomhedMetadata.NyesteVirksomhedsform != null ? org.virksomhedMetadata.NyesteVirksomhedsform.KortBeskrivelse : null;

            result.FiscalYearStart              = this.GetCurrentValue(org.attributter.Where(a => a.Type == "REGNSKABSÅR_START").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);
            result.FiscalYearEnd                = this.GetCurrentValue(org.attributter.Where(a => a.Type == "REGNSKABSÅR_SLUT").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);

            DateTimeOffset dummy;
            result.FirstFiscalYearStart         = this.GetCurrentValue(org.attributter.Where(a => a.Type == "FØRSTE_REGNSKABSPERIODE_START").SelectMany(a => a.Vaerdier), v => v.Periode, v => DateTimeOffset.TryParse(v.Vaerdi, out dummy), v => (DateTimeOffset?)DateTimeOffset.Parse(v.Vaerdi), date);
            result.FirstFiscalYearEnd           = this.GetCurrentValue(org.attributter.Where(a => a.Type == "FØRSTE_REGNSKABSPERIODE_SLUT").SelectMany(a => a.Vaerdier), v => v.Periode, v => DateTimeOffset.TryParse(v.Vaerdi, out dummy), v => (DateTimeOffset?)DateTimeOffset.Parse(v.Vaerdi), date);

            result.Purpose                      = this.GetCurrentValue(org.attributter.Where(a => a.Type == "FORMÅL").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);
            result.RegisteredCapital            = this.GetCurrentValue(org.attributter.Where(a => a.Type == "KAPITAL").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);
            result.RegisteredCapitalCurrency    = this.GetCurrentValue(org.attributter.Where(a => a.Type == "KAPITALVALUTA").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);
            result.StatutesLastChanged          = this.GetCurrentValue(org.attributter.Where(a => a.Type == "VEDTÆGT_SENESTE").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);
            result.HasShareCapitalClasses       = this.GetCurrentValue(org.attributter.Where(a => a.Type == "KAPITALKLASSER").SelectMany(a => a.Vaerdier), v => v.Periode, v => true, v => v.Vaerdi, date);

            result.MainIndustry                 = org.virksomhedMetadata.NyesteHovedbranche != null ? new IndustryDescription(org.virksomhedMetadata.NyesteHovedbranche) : null;
            result.OtherIndustry1               = org.virksomhedMetadata.NyesteBibranche1 != null ? new IndustryDescription(org.virksomhedMetadata.NyesteBibranche1) : null;
            result.OtherIndustry2               = org.virksomhedMetadata.NyesteBibranche2 != null ? new IndustryDescription(org.virksomhedMetadata.NyesteBibranche2) : null;
            result.OtherIndustry3               = org.virksomhedMetadata.NyesteBibranche3 != null ? new IndustryDescription(org.virksomhedMetadata.NyesteBibranche3) : null;

            result.NumberOfEmployees            = this.GetEmploymentRange(org);

            return result;
        }

        private Range<int> GetEmploymentRange(Vrvirksomhed org)
        {
            DateTime latestEmployment = DateTime.MinValue;
            Range<int> employmentRange = null;

            if (org.virksomhedMetadata.NyesteAarsbeskaeftigelse != null)
            {
                var employment = org.virksomhedMetadata.NyesteAarsbeskaeftigelse;
                var date = new DateTime(employment.Aar, 1, 1);

                var rangeText = employment.IntervalKodeAntalAnsatte;
                var rangeMatch = Regex.Match(rangeText, @"^ANTAL_(?<from>\d+)_(?<to>\d+)$");

                if (rangeMatch.Success && date > latestEmployment)
                {
                    employmentRange  = new Range<int>(int.Parse(rangeMatch.Groups["from"].Value), int.Parse(rangeMatch.Groups["to"].Value));
                    latestEmployment = date;
                }
            }

            if (org.virksomhedMetadata.NyesteKvartalsbeskaeftigelse != null)
            {
                var employment = org.virksomhedMetadata.NyesteKvartalsbeskaeftigelse;
                var date = new DateTime(employment.Aar, employment.Kvartal * 3, 1);

                var rangeText = employment.IntervalKodeAntalAnsatte;
                if (rangeText != null)
                {
                    var rangeMatch = Regex.Match(rangeText, @"^ANTAL_(?<from>\d+)_(?<to>\d+)$");

                    if (rangeMatch.Success && date > latestEmployment)
                    {
                        employmentRange  = new Range<int>(int.Parse(rangeMatch.Groups["from"].Value), int.Parse(rangeMatch.Groups["to"].Value));
                        latestEmployment = date;
                    }
                }
            }

            if (org.virksomhedMetadata.NyesteMaanedsbeskaeftigelse != null)
            {
                var employment = org.virksomhedMetadata.NyesteMaanedsbeskaeftigelse;
                var date = new DateTime(employment.Aar, employment.Maaned, 1);

                var rangeText = employment.IntervalKodeAntalAnsatte;
                var rangeMatch = Regex.Match(rangeText, @"^ANTAL_(?<from>\d+)_(?<to>\d+)$");

                if (rangeMatch.Success && date > latestEmployment)
                {
                    employmentRange  = new Range<int>(int.Parse(rangeMatch.Groups["from"].Value), int.Parse(rangeMatch.Groups["to"].Value));
                    latestEmployment = date;
                }
            }

            return employmentRange;
        }

        private T2 GetCurrentValue<T, T2>(IEnumerable<T> items, Func<T, Periode> periodSelector, Func<T, bool> filter, Func<T, T2> selector, DateTime date)
            where T : class
        {
            return this.GetCurrentValue(items, periodSelector, filter, selector, default(T2), date);
        }

        private T2 GetCurrentValue<T, T2>(IEnumerable<T> items, Func<T, Periode> periodSelector, Func<T, bool> filter, Func<T, T2> selector, T2 defaultValue, DateTime date)
            where T : class
        {
            var currentValue = GetCurrentValue<T>(items, periodSelector, date);

            if (currentValue == null || !filter(currentValue))
                return defaultValue;

            return selector(currentValue);
        }

        private T GetCurrentValue<T>(IEnumerable<T> items, Func<T, Periode> selector, DateTime date)
            where T : class
        {
            if (items == null)
                return null;

            var currentValue = items.FirstOrDefault(i => this.IsCurrent(selector(i), date));

            return currentValue;
        }

        private bool IsCurrent(Periode period, DateTime date)
        {
            return period.GyldigFra <= date && (!period.GyldigTil.HasValue || period.GyldigTil.Value >= date);
        }
    }
}