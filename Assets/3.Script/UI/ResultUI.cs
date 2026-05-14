using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class ResultUI : MonoBehaviour
{
    public static ResultUI Instance { get; private set; }

    [Serializable]
    private class SurvivorResultSlot
    {
        public GameObject root;
        public Text nicknameText;
        public Text recordingTimeText;
        public GameObject[] evidences;
    }

    [Header("생존자 결과 슬롯")]
    [SerializeField] private SurvivorResultSlot[] survivorSlots;

    private readonly StringBuilder builder = new StringBuilder(32);

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ApplySurvivorResults(
        string[] nicknames,
        float[] recordTimes,
        int[] evidenceMasks,
        bool[] reachedResults)
    {
        if (survivorSlots == null)
            return;

        for (int i = 0; i < survivorSlots.Length; i++)
        {
            SurvivorResultSlot slot = survivorSlots[i];

            if (slot == null)
                continue;

            bool show =
                reachedResults != null &&
                i < reachedResults.Length &&
                reachedResults[i];

            if (slot.root != null)
                slot.root.SetActive(show);

            int evidenceMask =
                evidenceMasks != null && i < evidenceMasks.Length
                    ? evidenceMasks[i]
                    : 0;

            if (slot.evidences != null)
            {
                for (int j = 0; j < slot.evidences.Length; j++)
                {
                    if (slot.evidences[j] == null)
                        continue;

                    bool hasEvidence = show && (evidenceMask & (1 << j)) != 0;
                    slot.evidences[j].SetActive(hasEvidence);
                }
            }

            if (!show)
                continue;

            if (slot.nicknameText != null)
            {
                slot.nicknameText.text =
                    nicknames != null &&
                    i < nicknames.Length &&
                    !string.IsNullOrEmpty(nicknames[i])
                        ? nicknames[i]
                        : "NickName";
            }

            if (slot.recordingTimeText != null)
            {
                float recordTime =
                    recordTimes != null && i < recordTimes.Length
                        ? recordTimes[i]
                        : 0f;

                int totalSeconds = Mathf.FloorToInt(recordTime);
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;

                builder.Clear();
                builder.Append("Recording : ");

                if (minutes > 0)
                {
                    builder.Append(minutes);
                    builder.Append("m ");
                }

                builder.Append(seconds);
                builder.Append("s");

                slot.recordingTimeText.text = builder.ToString();
            }
        }
    }
}