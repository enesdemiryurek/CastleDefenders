# GAME DESIGN DOCUMENT (GDD)
Project Name: Castle Defenders (Co-op)
Engine: Unity 6 (URP)
Language: C#
Network Library: Mirror (Planned)




## KOD STANDARTLARI (Mühendislik Kuralları)
- Tüm değişkenler 'SerializeField private' olarak tanımlanacak.
- Spagetti kod yasak: Manager sistemi (GameManager, UnitManager) kullanılacak.
- SOLID prensiplerine dikkat edilecek.
🏰 PROJE: CASTLE DEFENDERS 
Tür: 4 Kişilik Co-op Taktiksel Savunma & Aksiyon Platform: PC (Steam) Motor: Unity 6 (URP) Takım: 2 Kişi (Developer + 3D Artist)
1. OYUNUN ÖZETİ (High Concept)
Arkadaşlarla toplanıp, kendi ordularımızı yöneterek kalemizi düşman dalgalarına karşı savunduğumuz bir oyun. Sıradan bir kule savunma değil; Commander Mode (Komutan Modu) oynuyoruz. Yani hem karakterimizle savaşın içinde kılıç sallıyoruz hem de emrimizdeki askerlere (Okçular, Kalkanlılar) anlık taktiksel emirler veriyoruz.
Oynanış & Hub: Savaşlar arasında oyuncular Hub Alanında (Kale İçi) bulunur. Burası UI (Menü) tabanlıdır ancak atmosferiktir.
Demirci & Zırhçı: Savaşta kazanılan ganimetlerle karakter geliştirilir.
Birlik Mağazası (Unit Store): Yeni asker tipleri kiralanır.
Sosyal Alan (Eşler): Oyuncuların oyunda birer "Eşi" (NPC) vardır. Onlarla girilen diyaloglar veya rastgele olaylar (Dedikodu sistemi), savaş sırasındaki buff/debuff'ları ve "Troll" olayları tetikler.
Savaş Hazırlığı: Her oyuncu savaşa girmeden önce envanterinden Sadece 3 Birlik seçer (Örn: 1. Kalkanlı, 2. Okçu, 3. Süvari). Savaş sırasında 1-2-3 tuşlarıyla bu birlikleri stratejik noktalara yerleştirir. 4 Oyuncu toplamda 12 birlik ve 4 kahraman ile devasa dalgalara karşı durur.
Hedef & Atmosfer: Oynanış hissi Conqueror's Blade gibi taktiksel, görsellik TABS/Low Poly gibi temiz ama daha "Cool/Tok" duran, atmosferi ise Sea of Thieves gibi arkadaşlar arası makaraya müsait bir yapı.
2. TEMEL MEKANİKLER (Core Mechanics)
A. Karakter Kontrolü (Player)
Kamera: 3. Şahıs (TPS) - Omuz Arkası (Bannerlord tarzı).
Hareket: WASD ile yürüme/koşma.
Aksiyon: Sol tık saldırı, Sağ tık savunma, Space zıplama.
B. Ordu Komuta Sistemi (Unit Commander)
Oyuncu sadece kendini değil, arkasındaki birliği de yönetir.
Tuş 1-2-3: Savaşa getirdiği 3 farklı birliği seçer.
Tuş X (Saldır/Git): Mouse imlecinin baktığı noktaya askerleri gönderir.
Tuş Z (Formasyon): Askerler olduğu yerde savunma pozisyonuna geçer (Kalkan duvarı).
Tuş C (Takip Et): Askerler komutanı (oyuncuyu) takip eder.
C. Düşman & Savaş (Combat Loop)
Dalga Sistemi (Wave): Düşmanlar belirli aralıklarla ve artan zorlukta doğar.
Akıllı Yapay Zeka: Düşmanlar kalenin "Taht Odasına" ulaşmaya çalışır, yolda gördüğü oyuncuya veya askerlere saldırır.
Kazanma: Tüm dalgaları temizle.
Kaybetme: Kalenin Tahtı/Kalbi yıkılırsa VEYA tüm oyuncular aynı anda ölürse.
3. SANAT VE GÖRSELLİK (Art Direction)
Sorumlu: 3D Artist
Tarz: Stylized Low Poly (Düşük Poligon ama Stilize).
Referans: Masaüstü minyatür savaş oyunları (Warhammer figürleri gibi).
Üretim Tekniği (Modular System):
1 Tane "Base Mesh" (Çıplak Manken) yapılacak.
Üstüne parça parça Zırh, Kask, Silah, Pelerin modellenecek.
Unity içinde bu parçalar birleştirilip onlarca farklı sınıf yaratılacak.
Renkler: Düz renkler (Flat Shading) + Unity Post-Processing (Bloom, Ambient Occlusion).
4. EĞLENCE & SOSYAL KAOS (The "Spice")
Oyunu arkadaşlar arasında efsane yapacak, hikaye tabanlı "Troll" mekanikler.
"Rüşvetçi Hain" Teklifi: Savaş öncesi gizli teklif: "Kapıyı 10 saniye açık bırak, 5000 altın senin." Kabul ederse zengin olur ama takımını satar. Sonuç: Oyuncunun adı o tur boyunca "Hain Sürtük" olarak değişir.
"Yasak Aşk / Gayrimeşru Çocuk": Bildirim düşer: "Lord Mehmet'in çocuğu Lord Ahmet'e benziyor!" Sonuç: Mağdur öfkelenir (Hasar artar), Suçlunun askerleri saygısızlaşır (Emir gecikir).
"Sahte Soylu" Olayı: Bir oyuncunun soylu olmadığı ortaya çıkar. Sonuç: Altın zırhı paslı teneke gibi görünür (Sadece görsel rezillik).
"Düşman Prensesin Gözdesi": Düşman komutanı bir oyuncuya aşık olur. Düşmanlar ona saldırmaz. Sonuç: Takım arkadaşları zorlanırken o rahat gezer, adı "Hain Sevgili" olur.
5. YOL HARİTASI (Roadmap - 1 Yıl)
Aşama 1: İskelet (1-3. Ay): Greyboxing (Kutu haritalar), Temel Savaş Kodu, Mirror Network Kurulumu.
Aşama 2: Giydirme (3-6. Ay): İlk 3D modellerin entegrasyonu, Animasyonlar, İlk "Eğlence" mekaniklerinin eklenmesi.
Aşama 3: İçerik ve Denge (6-9. Ay): Modular Harita sistemi, Farklı Düşman Tipleri (Boss vb.), UI/UX Tasarımı.
Aşama 4: Cila ve Final (9-12. Ay): Ses/Müzik, Efektler (VFX), Bug Temizliği, Steam Sayfası.



Assets/
│
├── _Project/
│   ├── _Scenes/
│   │   ├── Bootstrap.unity        # NetworkManager + Loader
│   │   ├── Lobby.unity            # Oda / Ready ekranı
│   │   ├── Game.unity             # Asıl savaş sahnesi
│   │
│   ├── _Scripts/
│   │   ├── Core/                  # Oyunun omurgası
│   │   │   ├── GameManager.cs
│   │   │   ├── NetworkGameManager.cs
│   │   │   └── SceneLoader.cs
│   │   │
│   │   ├── Network/               # SADECE network ile ilgili şeyler
│   │   │   ├── CustomNetworkManager.cs
│   │   │   ├── NetworkSpawner.cs
│   │   │   └── NetworkUtils.cs
│   │   │
│   │   ├── Player/
│   │   │   ├── PlayerController.cs
│   │   │   ├── PlayerCombat.cs
│   │   │   ├── PlayerNetwork.cs   # Cmd / Rpc burada
│   │   │   └── PlayerCamera.cs
│   │   │
│   │   ├── Units/
│   │   │   ├── UnitGroup.cs        # NETWORK OBJECT
│   │   │   ├── UnitAI.cs           # Server-side logic
│   │   │   ├── UnitVisual.cs       # Animasyon / mesh
│   │   │   └── UnitFormation.cs
│   │   │
│   │   ├── Enemies/
│   │   │   ├── EnemyAI.cs          # Server only
│   │   │   ├── EnemyCombat.cs
│   │   │   └── EnemyVisual.cs
│   │   │
│   │   ├── Combat/
│   │   │   ├── IDamageable.cs
│   │   │   ├── Health.cs
│   │   │   └── DamageSystem.cs
│   │   │
│   │   ├── Commands/
│   │   │   ├── CommandInput.cs     # X Z C inputları
│   │   │   └── CommandSender.cs    # Cmd çağrıları
│   │   │
│   │   ├── TrollSystem/
│   │   │   ├── TrollManager.cs
│   │   │   ├── TrollEventBase.cs
│   │   │   ├── TraitorEvent.cs
│   │   │   └── ForbiddenLoveEvent.cs
│   │   │
│   │   ├── UI/
│   │   │   ├── LobbyUI/
│   │   │   │   ├── LobbyPanel.cs
│   │   │   │   └── ReadyButton.cs
│   │   │   │
│   │   │   ├── HUD/
│   │   │   │   ├── HealthBarUI.cs
│   │   │   │   ├── UnitCommandUI.cs
│   │   │   │   └── NotificationUI.cs
│   │   │   │
│   │   │   └── Menus/
│   │   │       ├── BlacksmithUI.cs
│   │   │       └── UnitStoreUI.cs
│   │   │
│   │   ├── Systems/
│   │   │   ├── WaveSystem.cs
│   │   │   ├── SpawnSystem.cs
│   │   │   └── EconomySystem.cs
│   │   │
│   │   └── Utils/
│   │       ├── Singleton.cs
│   │       ├── ObjectPool.cs
│   │       └── Extensions.cs
│   │
│   ├── _Prefabs/
│   │   ├── Network/
│   │   │   ├── Player.prefab
│   │   │   ├── UnitGroup.prefab
│   │   │   └── Enemy.prefab
│   │   │
│   │   ├── Units/
│   │   │   ├── UnitVisual.prefab
│   │   │   └── FormationMarker.prefab
│   │   │
│   │   ├── UI/
│   │   │   ├── LobbyUI.prefab
│   │   │   └── HUD.prefab
│   │   │
│   │   └── Environment/
│   │       ├── Walls/
│   │       ├── Towers/
│   │       └── Props/
│   │
│   ├── _Art/
│   │   ├── Characters/
│   │   │   ├── BaseMesh/
│   │   │   ├── Armor/
│   │   │   └── Weapons/
│   │   │
│   │   ├── Environment/
│   │   │   ├── ModularCastle/
│   │   │   └── Terrain/
│   │   │
│   │   ├── Animations/
│   │   │   ├── Humanoid/
│   │   │   └── Enemies/
│   │   │
│   │   └── VFX/
│   │       ├── Trails/
│   │       └── Blood/
│   │
│   ├── _Materials/
│   │   ├── Characters/
│   │   ├── Environment/
│   │   └── VFX/
│   │
│   ├── _UI/
│   │   ├── Fonts/
│   │   ├── Icons/
│   │   └── Sprites/
│   │
│   ├── _Audio/
│   │   ├── SFX/
│   │   └── Music/
│   │
│   ├── _Settings/
│   │   ├── URP/
│   │   ├── Input/
│   │   └── ScriptableObjects/
│   │
│   └── _ThirdParty/
│       ├── Mirror/
│       └── OtherAssets/
│
└── README.md
