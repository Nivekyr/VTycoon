using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using GTA;
using GTA.Math;
using GTA.UI;
using GTA.Native;
using LemonUI;
using LemonUI.Elements;
using LemonUI.Menus;
using System.Globalization;
using System.Drawing;
using Newtonsoft.Json;
using Numerics = System.Numerics;
using static VTycoon.VehicleManager;
using static VTycoon.ItemManager;

namespace VTycoon
{
    public class Main : Script
    {
        // Path to the CSV file
        private string csvPath = @"scripts\VTycoon\businessdata.csv";
        // Dictionary to store blips for each business
        private Dictionary<string, Blip> businessBlips = new Dictionary<string, Blip>();
        private Dictionary<string, int> businessTimers = new Dictionary<string, int>();

        private NotificationQueue notificationQueue = new NotificationQueue();

        public Numerics.BigInteger modMoney;
        private string currentMoneyString = "";
        private Numerics.BigInteger previousPlayerMoney = -1;

        public List<InventoryItem> inventoryItems= new List<InventoryItem>();

        private PlayerData playerData;

        // Array to store business data
        private BusinessData[] businessData;

        public ObjectPool pool = new ObjectPool();

        public static Main InstanceMain;

        // Blip colors
        private BlipColor purchasedColor = BlipColor.Green;
        private BlipColor unpurchasedColor = BlipColor.Red;

        private NativeMenu businessMenu;
        private NativeMenu upgradeBusinessMenu;
        private NativeMenu playerInventoryMenu;
        private const float InteractionRange = 10.0f; // La distance maximale à laquelle le joueur peut interagir avec l'entreprise
        private const Control InteractionKey = Control.Context; // La touche que le joueur doit appuyer pour interagir avec l'entreprise

        private Vector3 chamberOfCommerceLocation = new Vector3(-115.48f, -605.00f, 36.28f);

        public Main()
        {
            InstanceMain = this;
            LoadAllData();
            CreateBusinessMarkers();
            Blip businessUpgraderBlip = World.CreateBlip(chamberOfCommerceLocation);
            businessUpgraderBlip.Sprite = (BlipSprite)590;
            businessUpgraderBlip.Color = BlipColor.NetPlayer25;
            businessUpgraderBlip.Name = "Chamber of Commerce";

            Tick += OnTick;
            Interval = 1;
        }

        private void OnTick(object sender, EventArgs e)
        {
            pool.Process();
            notificationQueue.Process();
            Vector3 playerPosition = Game.Player.Character.Position;
            UpdateMoneyStringIfChanged();
            DisplayCustomMoney();

            if (playerPosition.DistanceTo(chamberOfCommerceLocation) <= InteractionRange)
            {
                if (Game.IsControlPressed(InteractionKey))
                {
                    if (upgradeBusinessMenu == null || !upgradeBusinessMenu.Visible)
                    {
                        CreateBusinessUpgradeMenu();
                    }
                }

            }

            if (businessMenu == null || !businessMenu.Visible)
            {
                BusinessData closestBusiness = null;
                float closestDistance = float.MaxValue;

                // Trouver l'entreprise la plus proche du joueur et vérifier si elle est à portée
                foreach (BusinessData business in businessData)
                {
                    float distance = Vector3.Distance(Game.Player.Character.Position, business.Position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestBusiness = business;
                    }
                }


                //Detect close Business 
                if (closestBusiness != null && IsPlayerInRange(closestBusiness))
                {
                    // Afficher un message à l'écran pour informer le joueur qu'il peut interagir avec l'entreprise
                    GTA.UI.Notification.Show($"Appuyez sur {InteractionKey} pour interagir avec {closestBusiness.Name}");

                    // Ouvrir le menu si le joueur appuie sur la touche d'interaction
                    if (Game.IsControlJustPressed(InteractionKey))
                    {
                        CreateBusinessMenu(closestBusiness);
                    }
                }
            }
            foreach (BusinessData business in businessData)
            {
                if (business.Purchased && business.StoreInventory > 0)
                {
                    if (Game.GameTime - businessTimers[business.Name] >= business.Interval * 1000)
                    {
                        GenerateRevenue(business);
                        businessTimers[business.Name] = Game.GameTime;
                    }
                }
            }
        }

        public string ConvertMoneyToString(Numerics.BigInteger money)
        {
            string[] suffixes = { "", "K", "M", "B", "T", "q", "Q", "s", "S", "O", "N", "D", "Ud", "Dd", "Td", "qd", "Qd", "sd", "Sd", "Od", "Nd" };
            int suffixIndex = 0;

            decimal moneyDecimal = (decimal)money;

            while (moneyDecimal >= 1000 && suffixIndex < suffixes.Length - 1)
            {
                moneyDecimal /= 1000;
                suffixIndex++;
            }

            return $"{moneyDecimal:N2}{suffixes[suffixIndex]}";
        }


        private void DisplayCustomMoney()
        {
            int screenX = 10; // Modifiez ces valeurs pour positionner l'affichage de l'argent sur l'écran
            int screenY = 10;

            GTA.UI.TextElement moneyText = new GTA.UI.TextElement(currentMoneyString + "$", new Point(screenX, screenY), 0.75f);

            moneyText.Color = Color.FromArgb(114, 204, 114); // Couleur verte similaire à celle de l'argent dans GTA 5

            // Changez la police d'écriture
            moneyText.Font = GTA.UI.Font.Pricedown;
            moneyText.Draw();
        }

        private void UpdateMoneyStringIfChanged()
        {
            if (modMoney != previousPlayerMoney)
            {
                currentMoneyString = ConvertMoneyToString(modMoney);
                previousPlayerMoney = modMoney;
            }
        }

        private void LoadBusinessData()
        {
            // Read all lines from the CSV file
            string[] lines = File.ReadAllLines(csvPath);

            // Create a new array to store business data
            businessData = new BusinessData[lines.Length - 1];

            // Parse each line of the CSV file and store the data in the businessData array
            for (int i = 1; i < lines.Length; i++)
            {
                string[] fields = lines[i].Split(',');

                List<InventoryItem> businessInventory = new List<InventoryItem>();

                if (fields.Length >= 17 && !string.IsNullOrEmpty(fields[16]))
                {
                    businessInventory = fields[16].Split('|').Select(x =>
                    {
                        string[] itemData = x.Split(':');
                        return new InventoryItem { Item = itemManagerInstance.GetItemByName(itemData[0]), Quantity = int.Parse(itemData[1]) };
                    }).ToList();
                }

                businessData[i - 1] = new BusinessData
                {
                    Name = fields[0],
                    Position = new Vector3(float.Parse(fields[1]), float.Parse(fields[2]), float.Parse(fields[3])),
                    BlipSprite = int.Parse(fields[4]),
                    Purchased = bool.Parse(fields[5]),
                    Price = Numerics.BigInteger.Parse(fields[6]),
                    Revenues = Numerics.BigInteger.Parse(fields[7]),
                    StoreInventory = float.Parse(fields[8]),
                    MaxStoreInventory = float.Parse(fields[9]),
                    PriceMultiplier = float.Parse(fields[10]),
                    Interval = int.Parse(fields[11]),
                    StockUpgradeLevel = int.Parse(fields[12]),
                    ItemPriceUpgradeLevel = int.Parse(fields[13]),
                    IntervalUpgradeLevel = int.Parse(fields[14]),
                    Type = fields[15],
                    BusinessInventory = businessInventory,
                    SubTypeItemThatCanBeSold = fields[17].Split('|').ToList(),
                    IsAutomaticRefill = bool.Parse(fields[18]),
                    Tier= int.Parse(fields[19]),
                };
            }
        }

        


        private void CreateBusinessMarkers()
        {
            int activeTier = GetActiveTier();
            foreach (BusinessData business in businessData)
            {
                if (business.Tier == activeTier)
                {
                    if (!businessBlips.ContainsKey(business.Name))
                    {
                        Blip businessBlip = World.CreateBlip(business.Position);
                        businessBlip.Sprite = (BlipSprite)business.BlipSprite;
                        businessBlip.Color = business.Purchased ? purchasedColor : unpurchasedColor;
                        businessBlip.IsShortRange = true;
                        if (business.Purchased)
                        {
                            businessBlip.Name = business.Name;
                        }
                        else
                        {
                            businessBlip.Name = business.Name + "Cost: " + ConvertMoneyToString(business.Price) + "$";

                        }

                        businessBlips.Add(business.Name, businessBlip);
                    }

                    if (business.Purchased)
                    {
                        businessTimers.Add(business.Name, Game.GameTime);
                    }
                }
            }
        }

        public int GetActiveTier()
        {
            int maxTier = businessData.Max(b => b.Tier);

            for (int currentTier = 1; currentTier <= maxTier; currentTier++)
            {
                int purchasedBusinessesInTier = businessData.Count(b => b.Tier == currentTier && b.Purchased);
                int totalBusinessesInTier = businessData.Count(b => b.Tier == currentTier);

                if (purchasedBusinessesInTier < totalBusinessesInTier)
                {
                    return currentTier;
                }
            }

            return maxTier;
        }




        private void UpgradeBusinessOfType(string businessType, Numerics.BigInteger upgradeCost)
        {

            if (modMoney >= upgradeCost)
            {
                modMoney -= upgradeCost;
                if (!playerData.BusinessUpgrades.ContainsKey(businessType))
                {
                    playerData.BusinessUpgrades[businessType] = 1;
                }
                else
                {
                    playerData.BusinessUpgrades[businessType]++;
                }

                foreach (BusinessData business in businessData)
                {
                    if (business.Type == businessType)
                    {
                        business.PriceMultiplier += 0.25f;
                    }
                }
               
                SaveAllData(); 
                LoadAllData();
                upgradeBusinessMenu.Visible = false;
                upgradeBusinessMenu = null;

            }
            else
            {
                upgradeBusinessMenu.Visible = false;
                upgradeBusinessMenu = null;
            }
        }


        private Numerics.BigInteger CalculateBusinessUpgradeCost(string businessType, int currentUpgradeLevel)
        {
            Numerics.BigInteger totalBusinessPrice = 0;

            // Parcourez la liste des business et additionnez les prix des business du type spécifié
            foreach (BusinessData business in businessData)
            {
                if (business.Type == businessType)
                {
                    totalBusinessPrice += business.Price;
                }
            }

            Numerics.BigInteger upgradeCost = totalBusinessPrice * 10 * (currentUpgradeLevel + 1);

            return upgradeCost;
        }

        private void CreateBusinessUpgradeMenu()
        {
            upgradeBusinessMenu = new NativeMenu("Améliorations business");
            pool.Add(upgradeBusinessMenu);
            List<string> businessTypes = businessData.Select(b => b.Type).Distinct().ToList();
            foreach (string businessType in businessTypes)
            {
                // Ajoutez un NativeItem pour chaque type de business
                NativeItem businessUpgradeItem = new NativeItem($"Améliorer {businessType}", $"Augmente le prix des articles des {businessType} de 50%. Cout: {ConvertMoneyToString(CalculateBusinessUpgradeCost(businessType, playerData.BusinessUpgrades[businessType]))}");
                upgradeBusinessMenu.Add(businessUpgradeItem);

                // Définissez les actions appropriées lors de la sélection des NativeItems
                businessUpgradeItem.Activated += (sender, selectedItem) => UpgradeBusinessOfType(businessType, CalculateBusinessUpgradeCost(businessType, playerData.BusinessUpgrades[businessType]));
            }
            upgradeBusinessMenu.Visible = true;
        }

        private bool IsPlayerInRange(BusinessData business)
        {
            return Vector3.Distance(Game.Player.Character.Position, business.Position) <= InteractionRange;
        }

        private void CreateBusinessMenu(BusinessData business)
        {
            // Créer un nouveau menu avec le nom de l'entreprise
            businessMenu = new NativeMenu(business.Name, "Interact with the business");

            // Ajouter le menu au pool
            pool.Add(businessMenu);

            if (!business.Purchased)
            {
                // Ajouter un bouton "Acheter" si le business n'est pas acheté
                NativeItem buyButton = new NativeItem("Acheter", $"Acheter ce business pour ${ConvertMoneyToString(business.Price)}");
                businessMenu.Add(buyButton);
                buyButton.Activated += (sender, selectedItem) => BuyBusiness(business);
            }
            else
            {
                // Ajouter un bouton "Collecter l'argent" si le business est acheté
                NativeItem collectButton = new NativeItem("Collecter l'argent", $"Collecter {ConvertMoneyToString(business.Revenues)}$");
                businessMenu.Add(collectButton);
                collectButton.Activated += (sender, selectedItem) => CollectRevenue(business);


                // Ajouter un bouton "Améliorer les stocks"
                NativeItem stockUpgradeButton = new NativeItem("Améliorer les stocks", $"Augmenter la taille des stocks. Coût: {ConvertMoneyToString(CalculateUpgradeCost(business, business.StockUpgradeLevel))}$. Stock maximum actuel: {business.MaxStoreInventory}");
                businessMenu.Add(stockUpgradeButton);
                stockUpgradeButton.Activated += (sender, selectedItem) => UpgradeStock(business);

                // Ajouter un bouton "Améliorer le prix des articles"
                NativeItem itemPriceUpgradeButton = new NativeItem("Améliorer le prix des articles", $"Augmenter le prix des articles. Coût: {ConvertMoneyToString(CalculateUpgradeCost(business, business.ItemPriceUpgradeLevel))}$. Price multiplier : {business.PriceMultiplier} ");
                businessMenu.Add(itemPriceUpgradeButton);
                itemPriceUpgradeButton.Activated += (sender, selectedItem) => UpgradeItemPrice(business);

                // Ajouter un bouton "Réduire l'intervalle"
                NativeItem intervalUpgradeButton = new NativeItem("Réduire l'intervalle", $"Réduire l'intervalle de génération d'argent. Coût: {ConvertMoneyToString(CalculateUpgradeCost(business, business.IntervalUpgradeLevel))}$. Intervalle actuelle: {business.Interval}");
                businessMenu.Add(intervalUpgradeButton);
                intervalUpgradeButton.Activated += (sender, selectedItem) => UpgradeInterval(business);

                NativeItem automaticRefillUpgradeButton = new NativeItem("Make the business automatic refill", $"Cost : {ConvertMoneyToString(business.Price * 1000)}$");
                businessMenu.Add(automaticRefillUpgradeButton);
                automaticRefillUpgradeButton.Activated += (sender, selectedItem) => { business.IsAutomaticRefill = true;modMoney -= business.Price * 1000;  };

                playerInventoryMenu = new NativeMenu("Inventaire du joueur", "Déposer des objets dans le business", $"{business.StoreInventory}/{business.MaxStoreInventory}".ToString());
                NativeSubmenuItem playerInventorySubmenu = businessMenu.AddSubMenu(playerInventoryMenu);
                pool.Add(playerInventoryMenu);

                // Ajoutez des éléments d'inventaire filtrés en fonction du type de business au sous-menu
                foreach (var inventoryItem in inventoryItems)
                {
                    if (business.SubTypeItemThatCanBeSold.Contains(inventoryItem.Item.SubType))
                    {
                        NativeItem item = new NativeItem(inventoryItem.Item.Name + " x1", inventoryItem.Quantity.ToString()) ;
                        playerInventoryMenu.Add(item);
                        item.Activated += (sender, selectedItem) => DepositItemInBusiness(inventoryItem, business, 1);
                        NativeItem itemall = new NativeItem(inventoryItem.Item.Name + " xAll", inventoryItem.Quantity.ToString());
                        playerInventoryMenu.Add(itemall);
                        itemall.Activated += (sender, selectedItem) => DepositItemInBusiness(inventoryItem, business, inventoryItem.Quantity);
                        
                    }
                }

            }


            // Ouvrir le menu
            businessMenu.Visible = true;
        }

        private void DepositItemInBusiness(InventoryItem inventoryItem, BusinessData business, int quantity)
        {
            // Vérifiez si l'élément existe déjà dans l'inventaire du business
            InventoryItem existingItem = business.BusinessInventory.FirstOrDefault(x => x.Item == inventoryItem.Item);

            // Calculez le poids total de l'élément à déposer
            float itemWeight = inventoryItem.Item.Weight * quantity;

            // Vérifiez si le dépôt de l'article dépasse la capacité maximale du stock
            if (business.StoreInventory + itemWeight > business.MaxStoreInventory)
            {
                Notification.Show("This business storage is full!");
                return; // Ne pas ajouter l'élément et quitter la fonction
            }


            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                business.BusinessInventory.Add(new InventoryItem { Item = inventoryItem.Item, Quantity = quantity });
            }

            // Mettez à jour l'inventaire du joueur en diminuant la quantité de l'élément déposé
            inventoryItem.Quantity -= quantity;
            itemManagerInstance.actualWeight -= itemWeight;
            // Si la quantité de l'élément dans l'inventaire du joueur est égale à 0, supprimez-le
            if (inventoryItem.Quantity == 0)
            {
                InstanceMain.inventoryItems.Remove(inventoryItem);
                Notification.Show("You don't have any more of this item to deposit.");
            }

            // Mettez à jour le stock du business
            business.StoreInventory = business.BusinessInventory.Sum(item => item.Item.Weight * item.Quantity);

            // Sauvegardez les données du joueur et du business
            SaveAllData();
            businessMenu.Visible = false;
            playerInventoryMenu.Visible = false;
        }




        private void UpgradeStock(BusinessData business)
        {
            Numerics.BigInteger upgradeCost = CalculateUpgradeCost(business, business.StockUpgradeLevel);
            if (modMoney >= upgradeCost)
            {
                modMoney -= upgradeCost;
                business.StockUpgradeLevel++;
                business.MaxStoreInventory += 10f; // Augmenter la taille des stocks de 10 (ou la valeur que vous préférez)
                SaveAllData();
                businessMenu.Visible = false;
                businessMenu = null;
            }
            else
            {
                GTA.UI.Notification.Show("Vous n'avez pas assez d'argent pour acheter cette amélioration.");
            }
        }

        private void UpgradeItemPrice(BusinessData business)
        {
            Numerics.BigInteger upgradeCost = CalculateUpgradeCost(business, business.ItemPriceUpgradeLevel);
            if (modMoney >= upgradeCost)
            {
                modMoney -= upgradeCost;
                business.ItemPriceUpgradeLevel++;
                business.PriceMultiplier += 0.05f; // Mettez à jour le multiplicateur
                SaveAllData();
                businessMenu.Visible = false;
                businessMenu = null;
            }
            else
            {
                GTA.UI.Notification.Show("Vous n'avez pas assez d'argent pour acheter cette amélioration.");
            }
        }


        private void UpgradeInterval(BusinessData business)
        {
            Numerics.BigInteger upgradeCost = CalculateUpgradeCost(business, business.IntervalUpgradeLevel);
            if (modMoney >= upgradeCost)
            {
                modMoney -= upgradeCost;
                business.IntervalUpgradeLevel++;
                business.Interval -= 1;
                if (business.Interval < 1) // Assurez-vous que l'intervalle ne devient pas négatif
                {
                    business.Interval = 1;
                }
                SaveAllData();
                businessMenu.Visible = false;
                businessMenu = null;
            }
            else
            {
                GTA.UI.Notification.Show("Vous n'avez pas assez d'argent pour acheter cette amélioration.");
            }
        }

        private void GenerateRevenue(BusinessData business)
        {
            if (!business.IsAutomaticRefill)
            {
                if (business.StoreInventory > 0 && business.BusinessInventory.Count > 0)
                {
                    // Sélectionnez un élément aléatoire de l'inventaire du business
                    Random random = new Random();
                    int randomIndex = random.Next(business.BusinessInventory.Count);
                    InventoryItem selectedItem = business.BusinessInventory[randomIndex];

                    // Déterminez la quantité vendue (modifiez la plage selon vos besoins)
                    int minQuantity = 1;
                    int maxQuantity = 3;
                    int soldQuantity = random.Next(minQuantity, maxQuantity + 1);

                    if (soldQuantity > selectedItem.Quantity)
                    {
                        soldQuantity = selectedItem.Quantity;
                    }
                    // Mettez à jour l'inventaire et les revenus du business
                    selectedItem.Quantity -= soldQuantity;
                    if (selectedItem.Quantity <= 0)
                    {
                        business.BusinessInventory.RemoveAt(randomIndex);
                    }

                    double revenue = (double)(selectedItem.Item.Price * soldQuantity) * business.PriceMultiplier;
                    business.Revenues += (Numerics.BigInteger)Math.Floor(revenue);
                    business.StoreInventory -= selectedItem.Item.Weight * soldQuantity;

                    if (business.StoreInventory == 0)
                    {
                        businessBlips[business.Name].Color = BlipColor.Orange;
                    }
                    else if (business.StoreInventory <= business.MaxStoreInventory / 2)
                    {
                        businessBlips[business.Name].Color = BlipColor.Yellow;
                    }
                    SaveAllData();
                }
            }
            else
            {
                // Obtenez la liste des items que le business peut vendre
                List<ItemData> itemsThatCanBeSold = itemManagerInstance.ItemsList.Where(item => business.SubTypeItemThatCanBeSold.Contains(item.SubType)).ToList();

                if (itemsThatCanBeSold.Count > 0)
                {
                    Random random = new Random();

                    // Sélectionnez un élément aléatoire parmi ceux qui peuvent être vendus
                    int randomIndex = random.Next(itemsThatCanBeSold.Count);
                    ItemData selectedItem = itemsThatCanBeSold[randomIndex];

                    // Déterminez la quantité vendue (modifiez la plage selon vos besoins)
                    int minQuantity = 1;
                    int maxQuantity = 5;
                    int soldQuantity = random.Next(minQuantity, maxQuantity + 1);

                    // Mettez à jour les revenus du business
                    double revenue = (double)(selectedItem.Price * soldQuantity) * business.PriceMultiplier;
                    business.Revenues += (Numerics.BigInteger)Math.Floor(revenue);


                    SaveAllData();
                }
            }

        }




        private void BuyBusiness(BusinessData business)
        {

            if (modMoney >= business.Price)
            {
                modMoney -= business.Price;
                business.Purchased = true;
                businessBlips[business.Name].Color = purchasedColor;
                businessBlips[business.Name].Name = business.Name;

                SaveAllData();

                businessTimers.Add(business.Name, Game.GameTime);

                businessMenu.Visible = false;
                businessMenu = null;
                UpdateBusinessBlips();

            }
            else
            {
                GTA.UI.Notification.Show("Vous n'avez pas assez d'argent pour acheter ce business.");
            }
        }

        private void CollectRevenue(BusinessData business)
        {
            // Ajoutez les revenus générés au solde du joueur et réinitialisez les revenus du business
            modMoney += business.Revenues;
            business.Revenues = 0;

            // Enregistrez les données mises à jour
            SaveAllData();

            businessMenu.Visible = false;
            businessMenu = null;
        }


        public void UpdateBusinessBlips()
        {
            int activeTier = GetActiveTier();

            // Mettez à jour les blips en fonction du tier actif
            foreach (BusinessData business in businessData)
            {
                if (business.Tier == activeTier && !businessBlips.ContainsKey(business.Name))
                {
                    Blip businessBlip = World.CreateBlip(business.Position);
                    businessBlip.Sprite = (BlipSprite)business.BlipSprite;
                    businessBlip.Color = business.Purchased ? purchasedColor : unpurchasedColor;
                    businessBlip.IsShortRange = true;
                    if (business.Purchased)
                    {
                        businessBlip.Name = business.Name;
                    }
                    else
                    {
                        businessBlip.Name = business.Name + " Cost: " + ConvertMoneyToString(business.Price) + "$";
                    }

                    businessBlips.Add(business.Name, businessBlip);
                }
            }
        }


        private void SaveBusinessData()
        {
            // Lire toutes les lignes du fichier CSV
            string[] lines = File.ReadAllLines(csvPath);

            // Créer une liste de nouvelles lignes CSV
            List<string> newLines = new List<string>();

            // Ajouter l'en-tête
            newLines.Add(lines[0]);

            // Parcourir chaque entreprise dans businessData
            foreach (BusinessData business in businessData)
            {
                // Convertir l'inventaire du business en chaîne formatée
                string inventoryString = string.Join("|", business.BusinessInventory.Select(i => i.Item.Name + ":" + i.Quantity));
                string subTypeItemThatCanBeSoldString = string.Join("|", business.SubTypeItemThatCanBeSold);

                // Mettre à jour la ligne avec les données de l'entreprise
                newLines.Add($"{business.Name},{business.Position.X},{business.Position.Y},{business.Position.Z},{business.BlipSprite},{business.Purchased},{business.Price},{business.Revenues},{business.StoreInventory},{business.MaxStoreInventory},{business.PriceMultiplier},{business.Interval},{business.StockUpgradeLevel},{business.ItemPriceUpgradeLevel},{business.IntervalUpgradeLevel},{business.Type},{inventoryString},{subTypeItemThatCanBeSoldString},{business.IsAutomaticRefill},{business.Tier}");
            }

            // Écrire les nouvelles lignes dans le fichier CSV
            File.WriteAllLines(csvPath, newLines);
            Console.WriteLine("Les données des business ont été sauvegardées");
        }


        private Numerics.BigInteger CalculateUpgradeCost(BusinessData business, Numerics.BigInteger currentLevel)
        {
            double baseCost = (double)business.Price * 0.5;
            double upgradeCost = baseCost + (baseCost * 10 * (double)currentLevel);
            return (Numerics.BigInteger)Math.Floor(upgradeCost);
        }



        public void SavePlayerData(PlayerData playerData, string filePath)
        {
            string jsonString = JsonConvert.SerializeObject(playerData, Formatting.Indented);
            File.WriteAllText(filePath, jsonString);
        }

        public void SaveAllData()
        {
            SaveBusinessData();
            playerData.Money = modMoney;
            playerData.Inventory = inventoryItems;
            SavePlayerData(playerData, "scripts\\VTycoon\\playerData.json");
        }

        public void LoadAllData()
        {
            LoadBusinessData();
            playerData = LoadPlayerData("scripts\\VTycoon\\playerData.json");
            modMoney = playerData.Money;
            inventoryItems = playerData.Inventory;

        }


        public PlayerData LoadPlayerData(string filePath)
        {
            if (File.Exists(filePath))
            {
                string jsonString = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<PlayerData>(jsonString);
            }

            PlayerData defaultPlayerData = new PlayerData();
            List<string> businessTypes = businessData.Select(b => b.Type).Distinct().ToList();
            foreach(string businessType in businessTypes)
            {
                defaultPlayerData.BusinessUpgrades[businessType] = 0;
            }
            // Initialisez le dictionnaire BusinessUpgrades avec les types de business uniques et un niveau d'amélioration par défaut de 0

            defaultPlayerData.Inventory = new List<InventoryItem>(); // Ajoutez cette ligne
            defaultPlayerData.Money = 5500;
            defaultPlayerData.StoredVehicles = new List<VehicleData>();
            return defaultPlayerData;
        }





        public class BusinessData
        {
            public string Name;
            public Vector3 Position;
            public int BlipSprite;
            public bool Purchased;
            public Numerics.BigInteger Price;
            public Numerics.BigInteger Revenues;
            public float StoreInventory;
            public float MaxStoreInventory;
            public float PriceMultiplier;
            public int Interval;
            public int StockUpgradeLevel;
            public int ItemPriceUpgradeLevel;
            public int IntervalUpgradeLevel;
            public string Type;
            public List<InventoryItem> BusinessInventory;
            public List<string> SubTypeItemThatCanBeSold { get; set; }
            public bool IsAutomaticRefill { get; set; }
            public int Tier { get; set; }
        }

    }
    public class NotificationQueue
    {
        private Queue<string> notifications = new Queue<string>();
        private int lastNotificationTime = 0;
        private const int NotificationInterval = 2500;

        public void Add(string notification)
        {
            notifications.Enqueue(notification);
        }

        public void Process()
        {
            if (notifications.Count > 0 && Game.GameTime - lastNotificationTime >= NotificationInterval)
            {
                string notification = notifications.Dequeue();
                GTA.UI.Notification.Show(notification);
                lastNotificationTime = Game.GameTime;
            }
        }
    }

    public class PlayerData
    {
        public Numerics.BigInteger Money { get; set; }
        public Dictionary<VehicleClass, int> VehicleStorageUpgrades { get; set; }
        public Dictionary<string, int> BusinessUpgrades { get; set; }
        public List<VehicleData> StoredVehicles { get; set; } // Ajoutez cette ligne

        public List<InventoryItem> Inventory;
        public PlayerData()
        {
            Inventory = new List<InventoryItem>();
            VehicleStorageUpgrades = new Dictionary<VehicleClass, int>();
            BusinessUpgrades = new Dictionary<string, int>();
            StoredVehicles = new List<VehicleData>(); // Initialisez la liste ici
        }
    }

}


