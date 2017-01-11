using UnityEngine;
using System.Collections;

public class PlayerAttributes : MonoBehaviour {
    public string _name;
    public string _topic;
    public float _concentration;
    public float _pullForce;
    public float _score;
    public float _flashState;

    public bool isSimulated() { return string.IsNullOrEmpty(_topic); }
}
