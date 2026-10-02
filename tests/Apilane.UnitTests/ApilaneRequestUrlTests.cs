using Apilane.Net;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Services;
using System.Collections.Specialized;
using System.Text.Json;
using System.Web;

namespace Apilane.UnitTests
{
    [TestClass]
    public class ApilaneRequestUrlTests
    {
        private const string ApiUrl = "https://api.test";

        // What the API receives: HttpClient sends Uri.PathAndQuery (a fragment never leaves the client)
        // and the server decodes each query value once.
        private static NameValueCollection ServerQuery(string url)
        {
            return HttpUtility.ParseQueryString(new Uri(url).Query);
        }

        private static JsonElement ServerJson(string url, string parameter)
        {
            var json = ServerQuery(url)[parameter];
            Assert.IsNotNull(json, $"'{parameter}' did not reach the server | {url}");
            return JsonDocument.Parse(json).RootElement;
        }

        private static string GetListUrl(string filterValue)
        {
            return DataGetListRequest.New("Subscriptions")
                .WithFilter(new FilterItem("CustomerId", FilterOperator.equal, filterValue))
                .WithSort(new SortItem() { Property = "Created", Direction = "DESC" })
                .GetUrl(ApiUrl);
        }

        // ─── Filter values ──────────────────────────────────────────────────────

        [TestMethod]
        [DataRow("cust_1")]
        [DataRow("cust%41")]                              // must not arrive as 'custA'
        [DataRow("100%")]
        [DataRow("%zz")]
        [DataRow("a#b")]                                  // must not cut the query off
        [DataRow("a&b=c")]
        [DataRow("a+b")]
        [DataRow("a b")]
        [DataRow("a/b?c")]
        [DataRow("O'Brien")]
        [DataRow("Dvořák 😀")]
        [DataRow("x%22,%22Operator%22:%22notequal")]      // must not rewrite the filter
        [DataRow("x%22}],%22Logic%22:%22OR")]
        [DataRow("a%2Cb")]                                // ',' and ':' are sent as they are, their escapes must still round-trip
        [DataRow("a%3Ab,c:d")]
        public void GetUrl_FilterValue_ReachesServerUnchanged(string value)
        {
            var url = GetListUrl(value);

            var filter = ServerJson(url, "filter");
            Assert.AreEqual(value, filter.GetProperty("Value").GetString());
            Assert.AreEqual("equal", filter.GetProperty("Operator").GetString());
            Assert.AreEqual("CustomerId", filter.GetProperty("Property").GetString());

            // The parameters around the filter survive too
            var query = ServerQuery(url);
            Assert.AreEqual("Subscriptions", query["Entity"]);
            Assert.AreEqual("1", query["pageIndex"]);
            Assert.AreEqual("Created", ServerJson(url, "sort").GetProperty("Property").GetString());
        }

        [TestMethod]
        [DataRow("cust%41")]
        [DataRow("a#b")]
        [DataRow("a b&c+d")]
        [DataRow("O'Brien (1)!*")]
        [DataRow("Dvořák 😀")]
        public void GetUrl_Uri_Does_Not_Change_The_Query(string value)
        {
            // Signed requests sign RequestUri.PathAndQuery; it must be exactly what GetUrl produced
            var url = GetListUrl(value);

            Assert.AreEqual(url.Substring(ApiUrl.Length), new Uri(url).PathAndQuery);
        }

        // ─── Other parameters ───────────────────────────────────────────────────

        [TestMethod]
        [DataRow("user+tag@test.com")]
        [DataRow("a&b#c@test.com")]
        public void GetUrl_Email_ReachesServerUnchanged(string email)
        {
            var url = AccountConfirmationEmailRequest.New(email).GetUrl(ApiUrl);

            Assert.AreEqual(email, ServerQuery(url)["email"]);
        }

        [TestMethod]
        public void GetUrl_Entity_And_Properties_ReachServerUnchanged()
        {
            var url = DataGetListRequest.New("My&Entity#1")
                .WithProperties("ID", "Na me", "A&b")
                .GetUrl(ApiUrl);

            var query = ServerQuery(url);
            Assert.AreEqual("My&Entity#1", query["Entity"]);
            Assert.AreEqual("ID,Na me,A&b", query["properties"]);
            Assert.AreEqual("20", query["pageSize"]);
        }

        [TestMethod]
        public void GetUrl_IdLists_Keep_Commas_And_Colons_Unescaped()
        {
            // Escaping ',' and ':' would only lengthen id lists and JSON filters (request lines are limited to 8 KB)
            var deleteUrl = DataDeleteRequest.New("Orders", new List<long>() { 3, 7, 42 }).GetUrl(ApiUrl);
            StringAssert.EndsWith(deleteUrl, "ids=3,7,42");

            var inUrl = DataGetListRequest.New("Orders")
                .WithFilter(new FilterItem("ID", FilterOperator.contains, "3,7,42"))
                .GetUrl(ApiUrl);
            StringAssert.Contains(inUrl, "%22Value%22:%223,7,42%22");
            Assert.AreEqual("3,7,42", ServerJson(inUrl, "filter").GetProperty("Value").GetString());
        }

        [TestMethod]
        public void GetUrl_PlainRequest_IsUnchanged()
        {
            var url = DataGetByIdRequest.New("Orders", 5).GetUrl(ApiUrl + "/");

            StringAssert.StartsWith(url, "https://api.test/api/Data/GetByID?");
            Assert.AreEqual("Orders", ServerQuery(url)["Entity"]);
            Assert.AreEqual("5", ServerQuery(url)["id"]);
        }

        // ─── ApilaneService.UrlFor_* ────────────────────────────────────────────

        // ─── No query parameters ────────────────────────────────────────────────

        [TestMethod]
        public void GetUrl_WithoutQueryParameters_HasNoTrailingQuestionMark()
        {
            // A bare '?' is dropped by the server but was part of the signed path on the client, so
            // signed requests to endpoints without parameters were always rejected.
            var url = AccountUserDataRequest.New().GetUrl(ApiUrl);

            Assert.AreEqual($"{ApiUrl}/api/Account/UserData", url);
            Assert.AreEqual("/api/Account/UserData", new Uri(url).PathAndQuery);
        }

        [TestMethod]
        [DataRow("user+tag@test.com")]
        [DataRow("a&AppToken=other#c@test.com")]
        public void UrlFor_Email_Encodes_The_Email(string email)
        {
            using var httpClient = new HttpClient();
            var service = new ApilaneService(httpClient, new ApilaneConfiguration()
            {
                ApplicationApiUrl = ApiUrl,
                ApplicationToken = "app-token"
            });

            foreach (var url in new[] { service.UrlFor_Email_ForgotPassword(email), service.UrlFor_Email_RequestConfirmation(email) })
            {
                var query = ServerQuery(url);
                Assert.AreEqual(email, query["Email"]);
                Assert.AreEqual("app-token", query["AppToken"]);
            }
        }
    }
}
