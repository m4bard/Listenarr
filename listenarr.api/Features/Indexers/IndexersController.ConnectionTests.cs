/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Listenarr.Api.Features.Indexers
{
    public partial class IndexersController
    {
        private async Task SaveTestResultAsync(Indexer indexer, bool persist, bool success, string? error)
        {
            // Update the passed indexer instance
            indexer.LastTestedAt = DateTime.UtcNow;
            indexer.LastTestSuccessful = success;
            indexer.LastTestError = error;

            if (persist && indexer.Id != 0)
            {
                // Persist test result back to the database for the stored indexer
                var existing = await _indexerRepository.GetByIdAsync(indexer.Id);
                if (existing != null)
                {
                    existing.LastTestedAt = indexer.LastTestedAt;
                    existing.LastTestSuccessful = success;
                    existing.LastTestError = error;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _indexerRepository.UpdateAsync(existing);
                }
            }
        }

        private async Task<IActionResult> ExecuteIndexerTestAsync(Indexer indexer, bool persist)
        {
            // Normalize URL first
            indexer.Url = IndexerUrlNormalizer.NormalizeIndexerUrl(indexer.Url);

            var impl = (indexer.Implementation ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                _logger.LogInformation("[IndexerTest] Testing indexer {Name} (impl={Impl}, url={Url})", LogRedaction.SanitizeText(indexer.Name), LogRedaction.SanitizeText(indexer.Implementation), LogRedaction.SanitizeUrl(indexer.Url));
                return impl switch
                {
                    var s when s == "internetarchive" || s == "internet archive" => await TestInternetArchive(indexer, persist),
                    var s when s == "myanonamouse" => await TestMyAnonamouse(indexer, persist),
                    // For Newznab/Torznab/Custom fall back to a generic connectivity check
                    _ => await TestGenericIndexer(indexer, persist)
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Indexer '{Name}' test failed", LogRedaction.SanitizeText(indexer.Name));
                return await BuildIndexerTestBadRequestAsync(indexer, persist, "Indexer test failed", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Indexer '{Name}' test failed", LogRedaction.SanitizeText(indexer.Name));
                return await BuildIndexerTestBadRequestAsync(indexer, persist, "Indexer test failed", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Indexer '{Name}' test failed", LogRedaction.SanitizeText(indexer.Name));
                return await BuildIndexerTestBadRequestAsync(indexer, persist, "Indexer test failed", ex);
            }
            catch (UriFormatException ex)
            {
                _logger.LogWarning(ex, "Indexer '{Name}' test failed", LogRedaction.SanitizeText(indexer.Name));
                return await BuildIndexerTestBadRequestAsync(indexer, persist, "Indexer test failed", ex);
            }
            catch (System.Net.CookieException ex)
            {
                _logger.LogWarning(ex, "Indexer '{Name}' test failed", LogRedaction.SanitizeText(indexer.Name));
                return await BuildIndexerTestBadRequestAsync(indexer, persist, "Indexer test failed", ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Indexer '{Name}' test failed", LogRedaction.SanitizeText(indexer.Name));
                return await BuildIndexerTestBadRequestAsync(indexer, persist, "Indexer test failed", ex);
            }
        }

        private async Task<IActionResult> BuildIndexerTestBadRequestAsync(Indexer indexer, bool persist, string message, Exception ex)
        {
            await SaveTestResultAsync(indexer, persist, false, ex.Message);
            return BadRequest(new
            {
                success = false,
                message,
                error = ex.Message,
                indexer = RedactIndexerForCaller(indexer)
            });
        }

        private async Task<IActionResult> TestGenericIndexer(Indexer indexer, bool persist)
        {
            var result = await _indexerTestWorkflow.TestGenericIndexerAsync(indexer, persist);
            if (result.Succeeded)
            {
                return Ok(new { success = true, message = result.Message, indexer = RedactIndexerForCaller(indexer) });
            }

            if (result.Status.HasValue)
            {
                return BadRequest(new { success = false, message = result.Message, status = result.Status.Value, indexer = RedactIndexerForCaller(indexer) });
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                return BadRequest(new { success = false, message = result.Message, error = result.Error, indexer = RedactIndexerForCaller(indexer) });
            }

            return BadRequest(new { success = false, message = result.Message, indexer = RedactIndexerForCaller(indexer) });
        }

        /// <summary>
        /// Test Internet Archive indexer connection
        /// </summary>
        private async Task<IActionResult> TestInternetArchive(Indexer indexer, bool persist)
        {
            var result = await _indexerTestWorkflow.TestInternetArchiveAsync(indexer, persist);
            if (result.Succeeded)
            {
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    collection = result.Collection,
                    indexer = RedactIndexerForCaller(indexer)
                });
            }

            return BadRequest(new
            {
                success = false,
                message = result.Message,
                error = result.Error,
                indexer = RedactIndexerForCaller(indexer)
            });
        }

        /// <summary>
        /// Test MyAnonamouse indexer connection
        /// </summary>
        private async Task<IActionResult> TestMyAnonamouse(Indexer indexer, bool persist)
        {
            var result = await _indexerTestWorkflow.TestMyAnonamouseAsync(indexer, persist);
            if (result.Succeeded)
            {
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    mam_id = RedactMamIdForCaller(result.MamId),
                    indexer = RedactIndexerForCaller(indexer)
                });
            }

            return BadRequest(new
            {
                success = false,
                message = result.Message,
                error = result.Error,
                indexer = RedactIndexerForCaller(indexer)
            });
        }
    }
}
