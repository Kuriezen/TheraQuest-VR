using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TheraQuest
{
    /// <summary>
    /// Talks to server.py. If the server can't be reached, the app keeps working:
    /// it uses a built-in default program and saves finished sessions to a local
    /// queue file that is uploaded the next time the server is available.
    /// </summary>
    public class ApiClient : MonoBehaviour
    {
        public string baseUrl = "http://127.0.0.1:8000";
        public int patientId = 1;
        public float timeoutSeconds = 4f;

        string QueuePath => Path.Combine(Application.persistentDataPath, "pending_sessions.jsonl");

        public IEnumerator LoadProgram(Action<ProgramResponse, bool> done)
        {
            using (var req = UnityWebRequest.Get($"{baseUrl}/api/patients/{patientId}/program"))
            {
                req.timeout = Mathf.CeilToInt(timeoutSeconds);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var data = JsonUtility.FromJson<ProgramResponse>(req.downloadHandler.text);
                    done(data, true);
                    yield return FlushQueue();
                }
                else
                {
                    Debug.LogWarning($"[TheraQuest] Server not reachable ({req.error}). Using default program.");
                    done(DefaultProgram(), false);
                }
            }
        }

        public IEnumerator UploadSession(SessionUpload upload, Action<SessionResponse> done)
        {
            string json = JsonUtility.ToJson(upload);
            SessionResponse resp = null;
            yield return Post(json, r => resp = r);
            if (resp == null)
            {
                File.AppendAllText(QueuePath, json + "\n");
                Debug.LogWarning("[TheraQuest] Session saved offline; it will upload when the server is back.");
            }
            done(resp);
        }

        IEnumerator Post(string json, Action<SessionResponse> done)
        {
            using (var req = new UnityWebRequest($"{baseUrl}/api/sessions", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(timeoutSeconds);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                    done(JsonUtility.FromJson<SessionResponse>(req.downloadHandler.text));
                else
                {
                    Debug.LogWarning($"[TheraQuest] Upload failed: {req.error} {req.downloadHandler?.text}");
                    done(null);
                }
            }
        }

        IEnumerator FlushQueue()
        {
            if (!File.Exists(QueuePath)) yield break;
            var lines = File.ReadAllLines(QueuePath);
            File.Delete(QueuePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                SessionResponse r = null;
                yield return Post(line, x => r = x);
                if (r == null) File.AppendAllText(QueuePath, line + "\n");
            }
        }

        public static ProgramResponse DefaultProgram()
        {
            return new ProgramResponse
            {
                patient = new PatientInfo { id = 1, code = "OFFLINE", name = "Offline test", level = 1, xpForNext = 100 },
                program = new ProgramInfo { id = 0, name = "Default program (offline)", days = new[] { 1, 3, 5 }, maxReps = 14 },
                exercises = new[]
                {
                    new ExerciseDef { id = 1, code = "shoulderAbd", name = "Shoulder abduction", joint = "Shoulder", measure = "shoulderElev", view = "front", position = "Standing, arm at your side, thumb up", side = "", targetAngle = 90, tolerance = 10, safetyMax = 120, sets = 1, restSeconds = 30, baseReps = 8, reps = 8, stage = 1, cue = "Raise your arm out to the side" },
                    new ExerciseDef { id = 2, code = "elbowFlex", name = "Elbow flexion", joint = "Elbow", measure = "elbowFlex", view = "side", position = "Standing, elbow at your side", side = "", targetAngle = 130, tolerance = 10, safetyMax = 150, sets = 1, restSeconds = 30, baseReps = 8, reps = 8, stage = 1, cue = "Bend your elbow and bring your hand up" },
                    new ExerciseDef { id = 3, code = "kneeExt", name = "Seated knee extension", joint = "Knee", measure = "kneeExt", view = "side", position = "Sitting tall, knee bent at 90", side = "", targetAngle = 80, tolerance = 10, sets = 1, restSeconds = 30, baseReps = 8, reps = 8, stage = 1, cue = "Sit tall and straighten your knee slowly" },
                }
            };
        }
    }
}
