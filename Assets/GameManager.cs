using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour {

    public List<GameObject> _players;
    public static GameManager gm;
	// Use this for initialization
	void Awake () {
        if (gm == null)
            gm = this; 

        
        // Get the players from the server
	
	}
	
	// Update is called once per frame
	void Update () {
	
	}
}
