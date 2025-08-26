using System.Collections.Generic;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public List<GameObject> enemies = new List<GameObject>();
    public List<Spawnpoint> spawnpoints = new List<Spawnpoint>();

    public VillageArea villageArea;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            // Example: spawn a new enemy at a random spawn point
            GameObject zombiePrefab = enemies[0];
            if (zombiePrefab != null)
            {
                SpawnAtRandomPoint(zombiePrefab);
            }
            else
            {
                Debug.LogError("ZombiePrefab not found in Resources folder.");
            }
        }
    }

    void SpawnEnemy(GameObject enemyPrefab, Vector3 position)
    {
        GameObject newEnemy = Instantiate(enemyPrefab, position, Quaternion.identity);
        var z = newEnemy.GetComponent<ZombieAI>();
        z.villageCenter = villageArea.transform;
        z.villageSize = new Vector2(villageArea.x, villageArea.z);
        enemies.Add(newEnemy);
    }

    void SpawnAtRandomPoint(GameObject enemyPrefab)
    {
        if (spawnpoints.Count == 0) return;

        Spawnpoint sp = spawnpoints[Random.Range(0, spawnpoints.Count)];
        Vector3 randomPos = sp.transform.position + Random.insideUnitSphere * sp.area;
        randomPos.y = sp.transform.position.y; // keep original height

        SpawnEnemy(enemyPrefab, randomPos);
    }
}
