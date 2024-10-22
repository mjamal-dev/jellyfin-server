namespace Jellyfin.Data.Enums
{
    /// <summary>
    /// An enum representing the different options for the home screen sections.
    /// </summary>
    public enum HomeSectionType
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        /// My Media.
        /// </summary>
        SmallLibraryTiles = 3,

        /// <summary>
        /// My Media Small.
        /// </summary>
        LibraryButtons = 8,

        /// <summary>
        /// Active Recordings.
        /// </summary>
        ActiveRecordings = 5,

        /// <summary>
        /// Continue Watching.
        /// </summary>
        Resume = 1,

        /// <summary>
        /// Continue Listening.
        /// </summary>
        ResumeAudio = 6,

        /// <summary>
        /// Latest Media.
        /// </summary>
        LatestMedia = 2,

        /// <summary>
        /// Next Up.
        /// </summary>
        NextUp = 4,

        /// <summary>
        /// Live TV.
        /// </summary>
        LiveTv = 7,

        /// <summary>
        /// Continue Reading.
        /// </summary>
        ResumeBook = 9
    }
}
