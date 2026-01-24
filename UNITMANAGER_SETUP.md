# UnitManager Kurulum Rehberi

## 1. Tag Oluşturma (ÖNEMLİ!)

Unity Editor'de şu tag'leri oluşturmalısın:

**Edit → Project Settings → Tags and Layers**

Şu tag'leri ekle:
- `Group1Unit` → Kalkanlılar grubu birimleri
- `Group2Unit` → Okçular grubu birimleri
- `Group3Unit` → Şövalye grubu birimleri

## 2. Birim Oluşturma

Her birim için:
1. 3D Cube veya Model ekle
2. **Tag** değiştir → `Group1Unit`, `Group2Unit` veya `Group3Unit`
3. **BaseUnit.cs** script'ini ekle
4. **NavMeshAgent** ekle
5. **Inspector** ayarları:
   - Unit Name: "Asker 1", "Okçu 1" vb.
   - Max Health: 100
   - Speed: 5
   - Attack Range: 2
   - Attack Damage: 10
   - Group Number: 1, 2 veya 3

## 3. NavMesh Oluşturma

1. Zeminini seç
2. Inspector → "Bake" checkbox'ını işaretle
3. **Window → AI → Navigation** → Bake

## 4. Oyun Kurulumu

Player GameObject'ine:
- PlayerController.cs
- UnitCommander.cs
- CharacterController
- CameraHolder (Child)
- Main Camera (CameraHolder'ın Child'ı)

Sahnede bir boş GameObject oluştur:
- **UnitManager.cs** script'ini ekle
- `Auto Find Units On Start` checkbox'ını işaretle

## 5. Test

Play mode'a gir:
- **1, 2, 3** → Grup değiştir
- **X** → Raycast saldırısı
- **Z** → Savunma formasyonu
- **C** → Oyuncuyu takip
- **WASD** → Hareket, **Mouse** → Döndür

## Kontrol Ettirilecek Şeyler

```csharp
// UnitManager'ı manuel kullan:
UnitManager.Instance.SelectGroup(1);
UnitManager.Instance.CommandGroupToMove(1, new Vector3(5, 0, 5));
UnitManager.Instance.CommandGroupToAttack(1, targetTransform);
UnitManager.Instance.PrintStatus(); // Durumu göster
```

---

**Notlar:**
- NavMesh'siz birimler hareket edemez!
- Tag'ler hassas, tam yazılmalı
- Birim GameObject'lerine rigidbody LAZIM DEĞIL
