using Listenarr.Domain.Common;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Mocks.Api
{
    public class NzbgetApiMock : BaseApiMock
    {
        public static readonly string SINGLE_FILE_NZBGET = "101";
        public static readonly string MULTI_FILE_NZBGET = "202";

        // Constants for the JSON-RPC `listgroups` canned response — used by tests
        // exercising FetchDownloadsAsync against an "active" queue group (issue #618).
        public const string ACTIVE_DOWNLOAD_NZBID = "14053";
        public const int FILE_SIZE_MB = 622;
        public const int REMAINING_SIZE_MB = 311;

        public NzbgetApiMock()
        {
            AddRoute("xmlrpc", GetHistory, HttpMethod.Post);
            AddRoute("jsonrpc", GetJsonrpc, HttpMethod.Post);
        }

        private async Task<HttpResponseMessage> GetHistory(HttpRequestMessage request, CancellationToken ct)
        {
            var response = """
            <?xml version="1.0"?>
            <methodResponse>
                <params>
                    <param>
                        <value>
                            <array>
                                <data>
                                    <value>
                                        <struct>
                                            <member>
                                                <name>ID</name>
                                                <value><string>{{SINGLE_FILE_NZBGET}}</string></value>
                                            </member>
                                            <member>
                                                <name>DestDir</name>
                                                <value><string>{{ARBITRARY_PATH_1}}</string></value>
                                            </member>
                                        </struct>
                                    </value>
                                    <value>
                                        <struct>
                                            <member>
                                                <name>ID</name>
                                                <value><string>{{MULTI_FILE_NZBGET}}</string></value>
                                            </member>
                                            <member>
                                                <name>DestDir</name>
                                                <value><string>{{ARBITRARY_PATH_2}}</string></value>
                                            </member>
                                        </struct>
                                    </value>
                                </data>
                            </array>
                        </value>
                    </param>
                </params>
            </methodResponse>
            """;
            response = response.Replace("{{SINGLE_FILE_NZBGET}}", SINGLE_FILE_NZBGET);
            response = response.Replace("{{MULTI_FILE_NZBGET}}", MULTI_FILE_NZBGET);
            response = response.Replace("{{ARBITRARY_PATH_1}}", FileUtils.GetAbsolutePath("nzbget", "completed", "Book.m4b"));
            response = response.Replace("{{ARBITRARY_PATH_2}}", FileUtils.GetAbsolutePath("nzbget", "completed", "Book Folder"));
            return MockUtils.GetCannedResponse(response, "text/xml");
        }

        private async Task<HttpResponseMessage> GetJsonrpc(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct);

            if (body.Contains("\"method\":\"listgroups\"", StringComparison.Ordinal))
            {
                var listgroups = $$"""
                {
                  "version": "1.1",
                  "result": [
                    {
                      "NZBID": {{ACTIVE_DOWNLOAD_NZBID}},
                      "NZBName": "test.release",
                      "Status": "DOWNLOADING",
                      "FileSizeMB": {{FILE_SIZE_MB}},
                      "RemainingSizeMB": {{REMAINING_SIZE_MB}}
                    }
                  ]
                }
                """;
                return MockUtils.GetCannedResponse(listgroups);
            }

            // Other methods (e.g. `status`): an empty result is sufficient for
            // FetchDownloadsAsync to proceed into the listgroups call.
            return MockUtils.GetCannedResponse("""{"version":"1.1","result":{}}""");
        }
    }
}
