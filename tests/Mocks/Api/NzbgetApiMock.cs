using Listenarr.Domain.Common;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Mocks.Api
{
    public class NzbgetApiMock : BaseApiMock
    {
        public static readonly string SINGLE_FILE_NZBGET = "101";
        public static readonly string MULTI_FILE_NZBGET = "202";
        public static readonly string COMPLETED_FILE_NZBGET = "14054";
        public static readonly string COMPLETED_FILE_HISTORY_ID = "9001";

        public const string ACTIVE_DOWNLOAD_NZBID = "14053";
        public const int FILE_SIZE_MB = 622;
        public const int REMAINING_SIZE_MB = 311;

        public bool IncludeActiveQueueGroup { get; set; } = true;
        public string CompletedContentPath { get; set; } = FileUtils.GetAbsolutePath("nzbget", "completed", "Completed Book.m4b");

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
                                                <value><string>1001</string></value>
                                            </member>
                                            <member>
                                                <name>NZBID</name>
                                                <value><string>{{SINGLE_FILE_NZBGET}}</string></value>
                                            </member>
                                            <member>
                                                <name>NZBName</name>
                                                <value><string>Book</string></value>
                                            </member>
                                            <member>
                                                <name>Status</name>
                                                <value><string>SUCCESS/HEALTH</string></value>
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
                                                <value><string>1002</string></value>
                                            </member>
                                            <member>
                                                <name>NZBID</name>
                                                <value><string>{{MULTI_FILE_NZBGET}}</string></value>
                                            </member>
                                            <member>
                                                <name>NZBName</name>
                                                <value><string>Book Folder</string></value>
                                            </member>
                                            <member>
                                                <name>Status</name>
                                                <value><string>SUCCESS/HEALTH</string></value>
                                            </member>
                                            <member>
                                                <name>DestDir</name>
                                                <value><string>{{ARBITRARY_PATH_2}}</string></value>
                                            </member>
                                        </struct>
                                    </value>
                                    <value>
                                        <struct>
                                            <member>
                                                <name>ID</name>
                                                <value><string>{{COMPLETED_FILE_HISTORY_ID}}</string></value>
                                            </member>
                                            <member>
                                                <name>NZBID</name>
                                                <value><string>{{COMPLETED_FILE_NZBGET}}</string></value>
                                            </member>
                                            <member>
                                                <name>NZBName</name>
                                                <value><string>test.release</string></value>
                                            </member>
                                            <member>
                                                <name>Status</name>
                                                <value><string>SUCCESS/HEALTH</string></value>
                                            </member>
                                            <member>
                                                <name>FinalDir</name>
                                                <value><string>{{COMPLETED_CONTENT_PATH}}</string></value>
                                            </member>
                                            <member>
                                                <name>DestDir</name>
                                                <value><string>{{COMPLETED_DEST_DIR}}</string></value>
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
            response = response.Replace("{{COMPLETED_FILE_NZBGET}}", COMPLETED_FILE_NZBGET);
            response = response.Replace("{{COMPLETED_FILE_HISTORY_ID}}", COMPLETED_FILE_HISTORY_ID);
            response = response.Replace("{{ARBITRARY_PATH_1}}", FileUtils.GetAbsolutePath("nzbget", "completed", "Book.m4b"));
            response = response.Replace("{{ARBITRARY_PATH_2}}", FileUtils.GetAbsolutePath("nzbget", "completed", "Book Folder"));
            response = response.Replace("{{COMPLETED_CONTENT_PATH}}", CompletedContentPath);
            response = response.Replace("{{COMPLETED_DEST_DIR}}", Path.GetDirectoryName(CompletedContentPath) ?? string.Empty);
            return MockUtils.GetCannedResponse(response, "text/xml");
        }

        private async Task<HttpResponseMessage> GetJsonrpc(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct);

            if (body.Contains("\"method\":\"listgroups\"", StringComparison.Ordinal))
            {
                if (!IncludeActiveQueueGroup)
                {
                    return MockUtils.GetCannedResponse("""{"version":"1.1","result":[]}""");
                }

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

            return MockUtils.GetCannedResponse("""{"version":"1.1","result":{}}""");
        }
    }
}
