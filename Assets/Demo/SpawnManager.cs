using System;
using UnityEngine;
using UnityEngine.UI;

public class SpawnManager : MonoBehaviour 
{
    public GameObject[] spawnPrefab;
    public int count;
    public Text m_Text;
    private void Start()
    {
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < spawnPrefab.Length; j++)
            {
                Instantiate(spawnPrefab[j], new Vector3(i * 2, 0, j * 2), Quaternion.identity);
            }
        }
    }

    public void Update()
    {
        m_Text.text = (1 / Time.deltaTime).ToString();
    }
}
