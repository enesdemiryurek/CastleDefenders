
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<BattleManager>();
                if (_instance == null)
                {
                    GameObject obj = new GameObject("BattleManager");
                    _instance = obj.AddComponent<BattleManager>();
                }
            }
            return _instance;
        }
    }
    private static BattleManager _instance;

    // Listeler: Tüm canlı birimleri burada tutuyoruz
    public List<UnitMovement> playerUnits = new List<UnitMovement>();
    public List<EnemyAI> enemyUnits = new List<EnemyAI>();
    public List<Transform> playerHeroes = new List<Transform>();

    private void Awake()
    {
        if (_instance == null) _instance = this;
        else if (_instance != this) Destroy(gameObject);
        
        // AUTO-SCAN: Sahnedeki her şeyi bul (Eğer önceden doğmuşlarsa)
        ScanBattlefield();
    }

    private void ScanBattlefield()
    {
        playerUnits.Clear();
        enemyUnits.Clear();
        playerHeroes.Clear();

        // 1. Oyuncu Askerlerini Bul
        var units = FindObjectsByType<UnitMovement>(FindObjectsSortMode.None);
        foreach (var u in units) RegisterPlayerUnit(u);

        // 2. Düşmanları Bul
        var enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (var e in enemies) RegisterEnemy(e);
        
        // 3. Kahramanları Bul (PlayerController)
        var heroes = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var h in heroes) RegisterHero(h.transform);
    }

    // --- REGISTER METHODS ---
    public void RegisterPlayerUnit(UnitMovement unit)
    {
        if (!playerUnits.Contains(unit)) playerUnits.Add(unit);
    }

    public void UnregisterPlayerUnit(UnitMovement unit)
    {
        if (playerUnits.Contains(unit)) playerUnits.Remove(unit);
    }

    public void RegisterEnemy(EnemyAI enemy)
    {
        if (!enemyUnits.Contains(enemy)) enemyUnits.Add(enemy);
    }

    public void UnregisterEnemy(EnemyAI enemy)
    {
        if (enemyUnits.Contains(enemy)) enemyUnits.Remove(enemy);
    }

    public void RegisterHero(Transform hero)
    {
        if (!playerHeroes.Contains(hero)) playerHeroes.Add(hero);
    }
    public void UnregisterHero(Transform hero)
    {
        if (playerHeroes.Contains(hero)) playerHeroes.Remove(hero);
    }


    // --- FINDING METHODS (Sadece Server Kullanmalı) ---

    // Askerlerin Düşman Bulması İçin (Player Unit -> arıyor -> Enemy Unit)
    public Transform GetNearestEnemyForUnit(Vector3 position, float maxRange = float.MaxValue)
    {
        Transform bestTarget = null;
        float closestIndSqr = maxRange * maxRange;

        foreach (var enemy in enemyUnits)
        {
            if (enemy == null) continue;
            
            float distSqr = (enemy.transform.position - position).sqrMagnitude;
            if (distSqr < closestIndSqr)
            {
                closestIndSqr = distSqr;
                bestTarget = enemy.transform;
            }
        }
        return bestTarget;
    }

    // Düşmanların Hedef Bulması İçin (Enemy AI -> arıyor -> Player Unit or Hero)
    public Transform GetNearestTargetForEnemy(Vector3 position, float maxRange = float.MaxValue)
    {
        Transform bestTarget = null;
        float closestIndSqr = maxRange * maxRange;

        // 1. Oyuncunun Askerlerine Bak
        foreach (var unit in playerUnits)
        {
            if (unit == null) continue;

            float distSqr = (unit.transform.position - position).sqrMagnitude;
            if (distSqr < closestIndSqr)
            {
                closestIndSqr = distSqr;
                bestTarget = unit.transform;
            }
        }

        // 2. Oyuncu Kahramanlarına (PlayerController) Bak
        foreach (var hero in playerHeroes)
        {
            if (hero == null) continue;

            float distSqr = (hero.transform.position - position).sqrMagnitude;
            
            // Kahramanlara öncelik/bonus verebiliriz veya eşit davranabiliriz.
            // Şimdilik eşit (en yakın kimse o).
            if (distSqr < closestIndSqr)
            {
                closestIndSqr = distSqr;
                bestTarget = hero;
            }
        }

        return bestTarget;
    }
}
