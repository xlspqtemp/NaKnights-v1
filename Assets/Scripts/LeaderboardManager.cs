using System;
using System.Collections.Generic;
using LootLocker;
using LootLocker.Requests;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Submits eligible run ratings and loads rows for the rating-score player leaderboard.
/// </summary>
public class LeaderboardManager : MonoBehaviour
{
    private const string LeaderboardKey = "rating_score";
    private const string RatingConfigResourceName = "RatingConfig";
    private const int LeaderboardEntryCount = 20;
    private const int LeaderboardStartOffset = 0;
    private const string OfflineMessage = "Leaderboards need an internet connection.";
    private const string SaveFailureMessage = "Couldn't save your score. Check your connection.";
    private const string GuestWinMessage = "Log in with an account to save your score.";
    private const string SavingScoreMessage = "Saving score...";
    private const string SavedScoreMessage = "Score saved!";
    private const string DisplayNameSuffix = "***";
    private const string EasyDifficultyName = "Easy";
    private const string NormalDifficultyName = "Normal";
    private const string MediumDifficultyName = "Medium";
    private const string HardDifficultyName = "Hard";
    private const string MissingDifficultyLabel = "-";
    private const string LegacyDifficultyLabel = "Legacy";
    private const string LocalGuestSessionKey = "localGuestSessionActive";
    private const string TutorialSeenPreferenceKey = "tutorialSeen";
    private const string TutorialSeenPlayerKeyPrefix = "tutorialSeenPlayer_";
    private const string WhiteLabelPlatformName = "WhiteLabel";

    [Serializable]
    private sealed class ScoreMetadata
    {
        public string letter;
        public string difficulty;
        public float daysSurvived;
    }

    private Text statusText;
    private Button retryButton;
    private Text[] rankTexts;
    private Text[] playerNameTexts;
    private Text[] letterTexts;
    private Image[] letterBadgeImages;
    private Text[] scoreTexts;
    private Text[] difficultyTexts;
    private Image[] rowBackgrounds;
    private string currentPlayerUlid;
    private string currentPlayerPublicUid;
    private bool requestInProgress;
    private bool scoreSubmissionAttempted;

    /// <summary>
    /// Binds the leaderboard UI and immediately starts loading scores.
    /// </summary>
    public void Initialize(Text requestStatusText, Button requestRetryButton, Text[] rowRanks,
        Text[] rowPlayers, Text[] rowLetters, Image[] rowLetterBadges, Text[] rowScores, Text[] rowDifficulties, Image[] backgrounds)
    {
        statusText = requestStatusText;
        retryButton = requestRetryButton;
        rankTexts = rowRanks;
        playerNameTexts = rowPlayers;
        letterTexts = rowLetters;
        letterBadgeImages = rowLetterBadges;
        scoreTexts = rowScores;
        difficultyTexts = rowDifficulties;
        rowBackgrounds = backgrounds;
        if (retryButton != null)
        {
            retryButton.onClick.AddListener(LoadScores);
        }

        LoadScores();
    }

    /// <summary>
    /// Refreshes the leaderboard when opened again or when Retry is pressed.
    /// </summary>
    public void LoadScores()
    {
        if (requestInProgress)
        {
            return;
        }

        ClearRows();
        if (Application.internetReachability == NetworkReachability.NotReachable ||
            !TryGetActivePlayer(out currentPlayerUlid, out LootLockerPlayerData currentPlayerData))
        {
            currentPlayerPublicUid = string.Empty;
            ShowStatus(OfflineMessage, true);
            return;
        }
        currentPlayerPublicUid = currentPlayerData.PublicUID;

        requestInProgress = true;
        ShowStatus("Loading...", false);
        LootLockerSDKManager.GetScoreList(LeaderboardKey, LeaderboardEntryCount, LeaderboardStartOffset, response =>
        {
            LootLockerLeaderboardMember[] entries = response != null
                ? response.items ?? Array.Empty<LootLockerLeaderboardMember>()
                : Array.Empty<LootLockerLeaderboardMember>();
            if (response == null)
            {
                Debug.LogWarning("[LEADERBOARD] GetScoreList result: response was null; entries=0.");
            }
            else
            {
                string errorMessage = response.errorData != null ? response.errorData.message : "none";
                Debug.Log($"[LEADERBOARD] GetScoreList result: success={response.success}, statusCode={response.statusCode}, entries={entries.Length}, errorData.message='{errorMessage}'.");
                int logCount = Mathf.Min(entries.Length, 5);
                for (int i = 0; i < logCount; i++)
                {
                    LootLockerLeaderboardMember entry = entries[i];
                    if (entry == null)
                    {
                        Debug.Log($"[LEADERBOARD] GetScoreList entry[{i}]=null.");
                        continue;
                    }

                    string playerName = entry.player != null ? entry.player.name ?? string.Empty : string.Empty;
                    string metadata = entry.metadata ?? string.Empty;
                    Debug.Log($"[LEADERBOARD] GetScoreList entry[{i}]: rank={entry.rank}, score={entry.score}, player='{playerName}', metadata='{metadata}'.");
                }
            }

            if (this == null)
            {
                return;
            }

            requestInProgress = false;
            if (response == null || !response.success)
            {
                ClearRows();
                ShowStatus(OfflineMessage, true);
                return;
            }

            if (entries.Length == 0)
            {
                ShowStatus("No scores yet", false);
                return;
            }

            ShowStatus(string.Empty, false);
            DisplayEntries(entries);
        });
    }

    /// <summary>Submits the final rating once for an eligible account-backed run.</summary>
    public void SubmitWinningScore(float finalWellness, Action<string> onStatus)
    {
        SubmitRunRating(onStatus);
    }

    /// <summary>Submits the final rating for a lost run.</summary>
    private void SubmitLostRunRating()
    {
        SubmitRunRating(null);
    }

    private void SubmitRunRating(Action<string> onStatus)
    {
        if (scoreSubmissionAttempted)
        {
            Debug.LogWarning("[LEADERBOARD] Duplicate run-rating submission was blocked.");
            return;
        }

        scoreSubmissionAttempted = true;
        if (IsLocalGuestSession())
        {
            Debug.Log("[LEADERBOARD] Score submission skipped: active session is a local-only Guest.");
            onStatus?.Invoke(GuestWinMessage);
            return;
        }

        if (!TryGetActivePlayer(out string playerUlid, out LootLockerPlayerData playerData))
        {
            Debug.LogWarning("[LEADERBOARD] Score submission skipped: no active LootLocker session.");
            onStatus?.Invoke(SaveFailureMessage);
            return;
        }

        LL_AuthPlatforms sessionPlatform = playerData.CurrentPlatform.Platform;
        if (sessionPlatform == LL_AuthPlatforms.Guest)
        {
            Debug.Log("[LEADERBOARD] Score submission skipped: active session is Guest.");
            onStatus?.Invoke(GuestWinMessage);
            return;
        }

        if (sessionPlatform != LL_AuthPlatforms.WhiteLabel)
        {
            Debug.LogWarning($"[LEADERBOARD] Score submission skipped: session type {sessionPlatform} is not WhiteLabel.");
            onStatus?.Invoke(GuestWinMessage);
            return;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Debug.LogWarning("[LEADERBOARD] Score submission skipped: network is not reachable.");
            onStatus?.Invoke(SaveFailureMessage);
            return;
        }

        RunStats runStats = RunStats.Instance != null ? RunStats.Instance : FindFirstObjectByType<RunStats>();
        if (runStats == null)
        {
            Debug.LogError("[LEADERBOARD] Rating submission skipped because the Game scene has no active RunStats recorder.");
            onStatus?.Invoke(SaveFailureMessage);
            return;
        }

        DayCounterUI dayCounter = FindFirstObjectByType<DayCounterUI>();
        string difficulty = DifficultySettings.CurrentStats != null
            ? DifficultySettings.CurrentStats.difficultyName
            : GetDifficultyNameForSelectedIndex(DifficultySettings.SelectedDifficultyIndex);
        float targetDays = dayCounter != null ? dayCounter.TotalGameDays : 14f;
        RatingConfig ratingConfig = Resources.Load<RatingConfig>(RatingConfigResourceName);
        RatingCalculator.RatingResult rating = RatingCalculator.Evaluate(runStats, difficulty, targetDays, ratingConfig);
        RunStats.RatingSnapshot snapshot = runStats != null ? runStats.GetRatingSnapshot() : null;
        float daysSurvived = snapshot != null ? snapshot.daysSurvived : 0f;
        string normalizedDifficulty = NormalizeDifficultyName(difficulty);
        if (string.IsNullOrEmpty(normalizedDifficulty))
            normalizedDifficulty = difficulty;

        int score = rating.TotalScore;
        string metadata = CreateRatingMetadata(rating, normalizedDifficulty, daysSurvived);
        Debug.Log($"[LEADERBOARD] Before submit: key={LeaderboardKey}, score={score}, memberId='', metadata={metadata}.");
        onStatus?.Invoke(SavingScoreMessage);

        LootLockerSDKManager.SubmitScore(string.Empty, score, LeaderboardKey, metadata, response =>
        {
            bool success = response != null && response.success;
            string errorMessage = response != null && response.errorData != null
                ? response.errorData.message ?? string.Empty
                : "none";
            string errorCode = response != null && response.errorData != null
                ? response.errorData.code ?? string.Empty
                : "none";
            string statusCode = response != null ? response.statusCode.ToString() : "null";
            Debug.Log($"[LEADERBOARD] Submit callback: success={success}, errorData.message='{errorMessage}', statusCode={statusCode}, errorData.code='{errorCode}'.");

            if (this == null)
                return;

            onStatus?.Invoke(success ? SavedScoreMessage : SaveFailureMessage);
        }, playerUlid);
    }

    private static WellnessManager subscribedWellnessManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeLostRunSubmissionHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeToActiveWellnessManager();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeToActiveWellnessManager();
    }

    private static void SubscribeToActiveWellnessManager()
    {
        WellnessManager activeWellnessManager = FindFirstObjectByType<WellnessManager>();
        if (subscribedWellnessManager == activeWellnessManager)
            return;

        if (subscribedWellnessManager != null)
            subscribedWellnessManager.OnGameLost -= HandleLostRun;
        subscribedWellnessManager = activeWellnessManager;
        if (subscribedWellnessManager != null)
            subscribedWellnessManager.OnGameLost += HandleLostRun;
    }

    private static void HandleLostRun(WellnessRunResult result)
    {
        WellnessManager activeWellnessManager = subscribedWellnessManager;
        if (activeWellnessManager == null)
            return;

        LeaderboardManager leaderboardManager = activeWellnessManager.GetComponent<LeaderboardManager>();
        if (leaderboardManager == null)
            leaderboardManager = activeWellnessManager.gameObject.AddComponent<LeaderboardManager>();
        leaderboardManager.SubmitLostRunRating();
    }

    /// <summary>Activates a local-only guest session without creating a cloud account.</summary>
    public static void ActivateLocalGuestSession()
    {
        PlayerPrefs.SetInt(LocalGuestSessionKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Clears local guest mode after a cloud-backed session successfully starts.</summary>
    public static void ClearLocalGuestSession()
    {
        PlayerPrefs.DeleteKey(LocalGuestSessionKey);
        PlayerPrefs.Save();
    }

    /// <summary>Returns whether the current game session was started as an offline local guest.</summary>
    public static bool IsLocalGuestSession()
    {
        return PlayerPrefs.GetInt(LocalGuestSessionKey, 0) == 1;
    }

    /// <summary>Returns whether the active session is a local or LootLocker guest session.</summary>
    public static bool IsActiveGuestSession()
    {
        if (IsLocalGuestSession())
            return true;

        return TryGetActivePlayer(out _, out LootLockerPlayerData playerData) &&
            string.Equals(playerData.CurrentPlatform.Platform.ToString(), "Guest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the active White Label account id for account-specific local progression.</summary>
    public static string GetActiveWhiteLabelPlayerId()
    {
        return TryGetWhiteLabelPlayer(out string playerUlid) ? playerUlid : string.Empty;
    }

    /// <summary>Returns whether the active account has completed its tutorial.</summary>
    public static bool HasSeenTutorialForActiveSession()
    {
        if (TryGetWhiteLabelPlayer(out string playerUlid))
        {
            string accountPreferenceKey = TutorialSeenPlayerKeyPrefix + playerUlid;
            if (PlayerPrefs.HasKey(accountPreferenceKey))
                return PlayerPrefs.GetInt(accountPreferenceKey, 0) != 0;

            bool hasSeenLegacyTutorial = PlayerPrefs.GetInt(TutorialSeenPreferenceKey, 0) != 0;
            if (hasSeenLegacyTutorial)
            {
                PlayerPrefs.SetInt(accountPreferenceKey, 1);
                PlayerPrefs.Save();
            }

            return hasSeenLegacyTutorial;
        }

        return PlayerPrefs.GetInt(TutorialSeenPreferenceKey, 0) != 0;
    }

    /// <summary>Records tutorial completion for the active account or local guest session.</summary>
    public static void MarkTutorialSeenForActiveSession()
    {
        if (TryGetWhiteLabelPlayer(out string playerUlid))
            PlayerPrefs.SetInt(TutorialSeenPlayerKeyPrefix + playerUlid, 1);
        else
            PlayerPrefs.SetInt(TutorialSeenPreferenceKey, 1);

        PlayerPrefs.Save();
    }

    /// <summary>Marks a newly registered White Label account as needing its first tutorial.</summary>
    public static void ResetTutorialSeenForPlayer(string playerUlid)
    {
        if (string.IsNullOrWhiteSpace(playerUlid))
            return;

        PlayerPrefs.SetInt(TutorialSeenPlayerKeyPrefix + playerUlid, 0);
        PlayerPrefs.Save();
    }

    private static bool TryGetWhiteLabelPlayer(out string playerUlid)
    {
        playerUlid = string.Empty;
        if (!TryGetActivePlayer(out string activePlayerUlid, out LootLockerPlayerData playerData) ||
            !string.Equals(playerData.CurrentPlatform.Platform.ToString(), WhiteLabelPlatformName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        playerUlid = activePlayerUlid;
        return true;
    }

    /// <summary>
    /// Returns the active player's LootLocker authentication platform for run diagnostics.
    /// </summary>
    public static string GetActiveSessionType()
    {
        if (IsLocalGuestSession())
            return "Guest";

        if (!TryGetActivePlayer(out _, out LootLockerPlayerData playerData))
        {
            return "NoActiveSession";
        }

        return playerData.CurrentPlatform.Platform.ToString();
    }

    /// <summary>
    /// Assigns a masked display name after a successful White Label login when no name exists.
    /// </summary>
    public static void EnsureWhiteLabelDisplayName(string email, string playerUlid, string existingPlayerName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(playerUlid) ||
            !string.IsNullOrWhiteSpace(existingPlayerName))
        {
            return;
        }

        LootLockerPlayerData playerData = LootLockerSDKManager.GetPlayerDataForPlayerWithUlid(playerUlid);
        if (playerData != null && !string.IsNullOrWhiteSpace(playerData.Name))
        {
            return;
        }

        int atIndex = email.IndexOf('@');
        string emailPrefix = atIndex >= 0 ? email.Substring(0, atIndex) : email;
        if (string.IsNullOrEmpty(emailPrefix))
        {
            return;
        }

        int prefixLength = Mathf.Min(3, emailPrefix.Length);
        string displayName = emailPrefix.Substring(0, prefixLength) + DisplayNameSuffix;
        LootLockerSDKManager.SetPlayerName(displayName, response =>
        {
            if (response == null || !response.success)
            {
                Debug.LogWarning("Could not set the player's leaderboard display name.");
            }
        }, playerUlid);
    }

    private static string CreateRatingMetadata(RatingCalculator.RatingResult rating, string difficulty, float daysSurvived)
    {
        string normalizedDifficulty = NormalizeDifficultyName(difficulty);
        if (string.IsNullOrEmpty(normalizedDifficulty))
            normalizedDifficulty = difficulty;

        return JsonUtility.ToJson(new ScoreMetadata
        {
            letter = rating.Letter,
            difficulty = normalizedDifficulty,
            daysSurvived = daysSurvived
        });
    }

    private static string GetDifficultyNameForSelectedIndex(int selectedIndex)
    {
        switch (selectedIndex)
        {
            case 0:
                return EasyDifficultyName;
            case 1:
                return NormalDifficultyName;
            case 2:
                return MediumDifficultyName;
            default:
                return HardDifficultyName;
        }
    }

    private static string NormalizeDifficultyName(string difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
        {
            return string.Empty;
        }

        string normalizedDifficulty = difficulty.Trim();
        if (string.Equals(normalizedDifficulty, EasyDifficultyName, StringComparison.OrdinalIgnoreCase))
        {
            return EasyDifficultyName;
        }

        if (string.Equals(normalizedDifficulty, NormalDifficultyName, StringComparison.OrdinalIgnoreCase))
        {
            return NormalDifficultyName;
        }

        if (string.Equals(normalizedDifficulty, MediumDifficultyName, StringComparison.OrdinalIgnoreCase))
        {
            return MediumDifficultyName;
        }

        if (string.Equals(normalizedDifficulty, HardDifficultyName, StringComparison.OrdinalIgnoreCase))
        {
            return HardDifficultyName;
        }

        return string.Empty;
    }

    private static bool TryGetActivePlayer(out string playerUlid, out LootLockerPlayerData playerData)
    {
        playerUlid = LootLockerSDKManager.GetDefaultPlayerUlid();
        playerData = null;
        if (string.IsNullOrWhiteSpace(playerUlid))
        {
            return false;
        }

        List<string> activePlayers = LootLockerSDKManager.GetActivePlayerUlids();
        bool isActive = false;
        if (activePlayers != null)
        {
            for (int i = 0; i < activePlayers.Count; i++)
            {
                if (string.Equals(activePlayers[i], playerUlid, StringComparison.OrdinalIgnoreCase))
                {
                    isActive = true;
                    break;
                }
            }
        }

        if (!isActive)
        {
            return false;
        }

        playerData = LootLockerSDKManager.GetPlayerDataForPlayerWithUlid(playerUlid);
        return playerData != null && !string.IsNullOrWhiteSpace(playerData.SessionToken);
    }

    private void DisplayEntries(LootLockerLeaderboardMember[] entries)
    {
        int displayCount = Mathf.Min(entries.Length, rankTexts != null ? rankTexts.Length : 0);
        for (int i = 0; i < displayCount; i++)
        {
            LootLockerLeaderboardMember entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            if (rankTexts[i] != null)
            {
                rankTexts[i].text = entry.rank.ToString();
                rankTexts[i].gameObject.SetActive(true);
            }

            if (playerNameTexts[i] != null)
            {
                string playerName = entry.player != null && !string.IsNullOrWhiteSpace(entry.player.name)
                    ? MaskEmailDisplayName(entry.player.name)
                    : entry.player != null && !string.IsNullOrWhiteSpace(entry.player.public_uid)
                        ? entry.player.public_uid
                        : "Player";
                playerNameTexts[i].text = playerName;
                playerNameTexts[i].gameObject.SetActive(true);
            }

            ScoreMetadata scoreMetadata = ParseScoreMetadata(entry.metadata);
            if (letterTexts != null && i < letterTexts.Length && letterTexts[i] != null)
            {
                letterTexts[i].text = scoreMetadata.letter;
                letterTexts[i].color = Color.white;
                letterTexts[i].gameObject.SetActive(true);
            }
            if (letterBadgeImages != null && i < letterBadgeImages.Length && letterBadgeImages[i] != null)
            {
                letterBadgeImages[i].color = GetLetterColor(scoreMetadata.letter);
                letterBadgeImages[i].gameObject.SetActive(true);
            }

            if (scoreTexts[i] != null)
            {
                scoreTexts[i].text = entry.score.ToString();
                scoreTexts[i].gameObject.SetActive(true);
            }

            if (difficultyTexts[i] != null)
            {
                difficultyTexts[i].text = scoreMetadata.difficulty;
                difficultyTexts[i].gameObject.SetActive(true);
            }

            if (rowBackgrounds != null && i < rowBackgrounds.Length && rowBackgrounds[i] != null)
            {
                bool isCurrentPlayer = entry.player != null &&
                    (string.Equals(entry.player.ulid, currentPlayerUlid, StringComparison.OrdinalIgnoreCase) ||
                     (!string.IsNullOrWhiteSpace(currentPlayerPublicUid) &&
                      string.Equals(entry.player.public_uid, currentPlayerPublicUid, StringComparison.OrdinalIgnoreCase)));
                rowBackgrounds[i].color = isCurrentPlayer
                    ? new Color(0.12f, 0.43f, 0.43f, 1f)
                    : i % 2 == 0
                        ? new Color(0.035f, 0.078f, 0.092f, 0.98f)
                        : new Color(0.025f, 0.06f, 0.073f, 0.98f);
                rowBackgrounds[i].gameObject.SetActive(true);
            }
        }
    }

    private static string MaskEmailDisplayName(string playerName)
    {
        int atIndex = playerName.IndexOf('@');
        if (atIndex < 0)
        {
            return playerName;
        }

        string prefix = playerName.Substring(0, atIndex);
        int prefixLength = Mathf.Min(3, prefix.Length);
        return prefix.Substring(0, prefixLength) + DisplayNameSuffix;
    }

    private static ScoreMetadata ParseScoreMetadata(string metadata)
    {
        ScoreMetadata missingMetadata = new ScoreMetadata
        {
            letter = MissingDifficultyLabel,
            difficulty = MissingDifficultyLabel,
            daysSurvived = 0f
        };
        if (string.IsNullOrWhiteSpace(metadata))
            return missingMetadata;

        try
        {
            ScoreMetadata parsed = JsonUtility.FromJson<ScoreMetadata>(metadata);
            if (parsed == null)
                return missingMetadata;

            string letter = string.IsNullOrWhiteSpace(parsed.letter) ? MissingDifficultyLabel : parsed.letter.Trim().ToUpperInvariant();
            if (letter != "S" && letter != "A" && letter != "B" && letter != "C" && letter != "D")
                letter = MissingDifficultyLabel;

            string difficulty = NormalizeDifficultyName(parsed.difficulty);
            return new ScoreMetadata
            {
                letter = letter,
                difficulty = string.IsNullOrEmpty(difficulty) ? MissingDifficultyLabel : difficulty,
                daysSurvived = parsed.daysSurvived
            };
        }
        catch (Exception)
        {
            return missingMetadata;
        }
    }

    private static Color GetLetterColor(string letter)
    {
        switch (letter)
        {
            case "S": return new Color(1f, 0.78f, 0.18f, 1f);
            case "A": return new Color(0.12f, 0.88f, 0.65f, 1f);
            case "B": return new Color(0.16f, 0.76f, 0.98f, 1f);
            case "C": return new Color(1f, 0.62f, 0.22f, 1f);
            case "D": return new Color(0.98f, 0.28f, 0.3f, 1f);
            default: return new Color(0.65f, 0.77f, 0.79f, 1f);
        }
    }

    private void ClearRows()
    {
        if (rankTexts == null)
        {
            return;
        }

        for (int i = 0; i < rankTexts.Length; i++)
        {
            SetRowText(rankTexts, i, string.Empty);
            SetRowText(playerNameTexts, i, string.Empty);
            SetRowText(letterTexts, i, string.Empty);
            SetRowText(scoreTexts, i, string.Empty);
            SetRowText(difficultyTexts, i, string.Empty);
            if (letterBadgeImages != null && i < letterBadgeImages.Length && letterBadgeImages[i] != null)
                letterBadgeImages[i].gameObject.SetActive(false);
            if (rowBackgrounds != null && i < rowBackgrounds.Length && rowBackgrounds[i] != null)
            {
                rowBackgrounds[i].gameObject.SetActive(false);
            }
        }
    }

    private static void SetRowText(Text[] texts, int index, string value)
    {
        if (texts != null && index < texts.Length && texts[index] != null)
        {
            texts[index].text = value;
            texts[index].gameObject.SetActive(false);
        }
    }

    private void ShowStatus(string message, bool showRetry)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(showRetry);
        }
    }
}
