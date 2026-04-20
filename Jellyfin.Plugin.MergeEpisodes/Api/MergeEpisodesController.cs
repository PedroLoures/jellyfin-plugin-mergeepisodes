using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MergeEpisodes.Api
{
    /// <summary>
    /// The Merge Episodes api controller.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("MergeEpisodes")]
    [Produces(MediaTypeNames.Application.Json)]
    public class MergeEpisodesController : ControllerBase
    {
        private readonly IEpisodeMergeService _mergeEpisodesManager;
        private readonly ILogger<MergeEpisodesController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeEpisodesController"/> class.
        /// </summary>
        /// <param name="mergeService">The episode merge service.</param>
        /// <param name="logger">The logger.</param>
        public MergeEpisodesController(
            IEpisodeMergeService mergeService,
            ILogger<MergeEpisodesController> logger)
        {
            _mergeEpisodesManager = mergeService;
            _logger = logger;
        }

        /// <summary>
        /// Scans all episodes and merges repeated ones.
        /// </summary>
        /// <response code="200">Merge completed. Returns operation result with counts and any failures.</response>
        /// <returns>An <see cref="OperationResult"/> with succeeded/failed counts and failed item names.</returns>
        [HttpPost("MergeEpisodes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> MergeEpisodesRequestAsync()
        {
            _logger.LogInformation("Starting a manual refresh, looking up for repeated versions");
            try
            {
                var result = await _mergeEpisodesManager.MergeEpisodesAsync(null).ConfigureAwait(false);
                _logger.LogInformation("Completed refresh");
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Merge episodes operation was cancelled");
                return Ok(new { message = "Operation cancelled", cancelled = true });
            }
        }

        /// <summary>
        /// Scans all episodes and splits merged ones (primary versions only).
        /// </summary>
        /// <response code="200">Split completed. Returns operation result with counts and any failures.</response>
        /// <returns>An <see cref="OperationResult"/> with succeeded/failed counts and failed item names.</returns>
        [HttpPost("SplitEpisodes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> SplitEpisodesRequestAsync()
        {
            _logger.LogInformation("Splitting all episodes");
            try
            {
                var result = await _mergeEpisodesManager.SplitEpisodesAsync(null).ConfigureAwait(false);
                _logger.LogInformation("Completed");
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Split episodes operation was cancelled");
                return Ok(new { message = "Operation cancelled", cancelled = true });
            }
        }

        /// <summary>
        /// Splits ALL episodes with any merge state (primary or secondary).
        /// Intended as a deep clean to fix issues left by older plugin versions.
        /// </summary>
        /// <response code="200">Split completed. Returns operation result with counts and any failures.</response>
        /// <returns>An <see cref="OperationResult"/> with succeeded/failed counts and failed item names.</returns>
        [HttpPost("SplitAllEpisodes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> SplitAllEpisodesRequestAsync()
        {
            _logger.LogInformation("Deep clean: splitting all episodes with any merge state");
            try
            {
                var result = await _mergeEpisodesManager.SplitAllEpisodesAsync(null).ConfigureAwait(false);
                _logger.LogInformation("Deep clean completed");
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Split all episodes operation was cancelled");
                return Ok(new { message = "Operation cancelled", cancelled = true });
            }
        }

        /// <summary>
        /// Cancels any currently running merge or split operation.
        /// </summary>
        /// <response code="204">Cancellation requested successfully.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating the cancellation was requested.</returns>
        [HttpPost("Cancel")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult CancelOperation()
        {
            _logger.LogInformation("Cancellation requested for running operation");
            _mergeEpisodesManager.CancelRunningOperation();
            return NoContent();
        }
    }
}
