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
using static VTycoon.Main;
using System.Diagnostics;
using static VTycoon.ItemManager;

namespace VTycoon
{
    public class ItemManager : Script
    {
        private string itemsJsonPath = "scripts\\VTycoon\\items.json";
        public List<ItemData> ItemsList;

        private DateTime lastPriceUpdate;
        private DateTime lastEventGeneration;
        public List<MarketEvent> MarketEvents { get; set; }
        public MarketEvent CurrentMarketEvent { get; set; }

        public List<MarketSeller> MarketSellers { get; set; }

        private List<CraftingRecipe> CraftingRecipes { get; set; }
        private List<CraftingLocation> CraftingLocations { get; set; }

        public static ItemManager itemManagerInstance;

        public float actualWeight;
        public float maxWeightOnPlayer = 50.0f;


        private Vector3 stockExchangeLocation = new Vector3(-157.25f, -604.48f, 48.24f);
        private const float InteractionRange = 10.0f; // La distance maximale à laquelle le joueur peut interagir avec l'entreprise
        private const Control InteractionKey = Control.Context; // La touche que le joueur doit appuyer pour interagir avec l'entreprise

        private const Control InventoryKey = Control.CinematicSlowMo;
        private NativeMenu stockExchangeMenu;
        private NativeMenu marketSellerMenu;
        private NativeMenu inventoryMenu;
        private NativeMenu craftingMenu;


        public ItemManager()
        {
            itemManagerInstance = this;
            LoadMarketSellersFromCsv();
            LoadMarketEventsFromCsv();
            CreateBlipsForMarketSellers(MarketSellers);
            LoadCraftingLocations("scripts\\VTycoon\\crafting_locations.csv");
            LoadCraftingRecipes("scripts\\VTycoon\\crating_recipes.csv");
            CreateBlipForCraftingLocation();
            ItemsList = LoadItemsFromJson();
            Blip stockExchangeBlip = World.CreateBlip(stockExchangeLocation);
            stockExchangeBlip.Sprite = (BlipSprite)682;
            stockExchangeBlip.Color = BlipColor.NetPlayer25;
            stockExchangeBlip.Name = "Stock Exchange Informations";
            Tick += OnTick;
            Interval = 1;
            lastPriceUpdate = DateTime.Now;

            foreach (ItemData item in ItemsList)
            {
                if (item.PriceHistory == null)
                {
                    item.PriceHistory = new List<PriceHistoryEntry>();
                }
            }
            
        }

        public ItemData GetItemByName(string name)
        {
            return ItemsList.FirstOrDefault(item => item.Name == name);
        }

        private void OnTick(object sender, EventArgs e)
        {
            Vector3 playerPosition = Game.Player.Character.Position;

            if (playerPosition.DistanceTo(stockExchangeLocation) <= InteractionRange)
            {
                if (Game.IsControlPressed(InteractionKey))
                {
                    if (stockExchangeMenu == null || !stockExchangeMenu.Visible)
                    {
                        CreateStockExchangeMenu();
                    }
                }
            }

            foreach(MarketSeller marketSeller in MarketSellers)
            {
                if (playerPosition.DistanceTo(marketSeller.Position) <= InteractionRange)
                {
                    if (Game.IsControlPressed(InteractionKey))
                    {
                        if (marketSellerMenu == null || !marketSellerMenu.Visible)
                        {
                            CreateMarketSellerMenu(marketSeller);
                        }
                    }
                }
            }

            foreach (CraftingLocation crafting in CraftingLocations)
            {
                if (playerPosition.DistanceTo(crafting.Position) <= InteractionRange)
                {
                    if (Game.IsControlPressed(InteractionKey))
                    {
                        if (craftingMenu == null || !craftingMenu.Visible)
                        {
                            CreateCraftingMenu(crafting);
                        }
                    }

                }
            }

            if (Game.IsControlJustPressed(InventoryKey))
            {
                if (inventoryMenu == null || !inventoryMenu.Visible)
                {
                    CreateInventoryMenu();
                }
            }

            if ((DateTime.Now - lastPriceUpdate).TotalHours >= 6 / 48.0) // Divisé par 48 pour convertir les heures réelles en heures de jeu
            {
                // Mettre à jour les prix
                UpdatePriceRandomly();
                Notification.Show("Every price has evolve !");

                // Mettre à jour le moment de la dernière mise à jour des prix
                lastPriceUpdate = DateTime.Now;
            }

            // Génère un nouvel événement si un jour de jeu s'est écoulé depuis la dernière génération
            if ((DateTime.Now - lastEventGeneration).TotalHours >= 12 / 48.0) // Divisé par 48 pour convertir les heures réelles en heures de jeu
            {
                // Générer un nouvel événement aléatoire
                GenerateRandomMarketEvent();

                // Afficher la notification de l'événement
                string listContent = string.Join(", ", CurrentMarketEvent.AffectedSubtypes);
                string impactColor = CurrentMarketEvent.Impact < 0 ? "#CC7272" : "#72CC72";
                string impactSign = CurrentMarketEvent.Impact >= 0 ? "+" : "";

                Notification.Show(CurrentMarketEvent.NewsMessage + " Variation on " + listContent + " = " + $"<font color=\"{impactColor}\">{impactSign}{CurrentMarketEvent.Impact}%</font>");

                // Mettre à jour le moment de la dernière génération d'événements
                lastEventGeneration = DateTime.Now;
            }

        }

        public void CreateBlipsForMarketSellers(List<MarketSeller> marketSellers)
        {
            foreach (MarketSeller marketSeller in marketSellers)
            {
                    Blip blip = World.CreateBlip(marketSeller.Position);
                    blip.Sprite = (BlipSprite)marketSeller.BlipSprite;
                    blip.Color = BlipColor.NetPlayer25;
                    blip.Name = marketSeller.Name;
                    blip.IsShortRange = true;
            }
        }

        private void CreateBlipForCraftingLocation()
        {

            foreach (CraftingLocation craftingLocation in CraftingLocations)
            {
                    Blip blip = World.CreateBlip(craftingLocation.Position);
                    blip.Sprite = (BlipSprite)craftingLocation.BlipSprite;
                    blip.Color = BlipColor.Pink;
                    blip.Name = craftingLocation.Name;
                    blip.IsShortRange = true;
            }
        }

        private void CreateCraftingMenu(CraftingLocation craftingLocation)
        {
            craftingMenu = new NativeMenu(craftingLocation.Name);
            InstanceMain.pool.Add(craftingMenu);

            foreach(string recipeName in craftingLocation.AvailableRecipes)
            {
                CraftingRecipe recipe = CraftingRecipes.FirstOrDefault(r => r.ResultItemName== recipeName);
                if (recipe != null)
                {
                    NativeItem recipeItem = new NativeItem(recipe.ResultItemName);
                    craftingMenu.Add(recipeItem);

                    recipeItem.Activated += (sender, args) => CraftItem(recipe);
                }
            }
            craftingMenu.Visible = true;
        }

        private void CreateStockExchangeMenu()
        {
            if (InstanceMain == null)
            {
                GTA.UI.Notification.Show("Erreur : mainScript ou mainScript.pool est null.");
                return;
            }

            if (ItemsList == null || ItemsList.Count == 0)
            {
                GTA.UI.Notification.Show("Erreur : ItemsList est null ou vide.");
                return;
            }
            stockExchangeMenu = new NativeMenu("Bourse des items");

            InstanceMain.pool.Add(stockExchangeMenu);

            // Obtenez la liste des types uniques à partir de la liste des items
            List<string> uniqueTypes = ItemsList.Select(item => item.Type).Distinct().ToList();

            // Parcourez les types d'items
            foreach (string itemType in uniqueTypes)
            {
                // Créez un nouveau menu pour afficher les items de ce type
                NativeMenu itemTypeMenu = new NativeMenu(itemType, itemType);
                InstanceMain.pool.Add(itemTypeMenu);

                // Ajoutez un élément de sous-menu pour la catégorie actuelle dans le menu principal
                NativeSubmenuItem itemTypeSubmenu = new NativeSubmenuItem(itemTypeMenu, stockExchangeMenu);
                stockExchangeMenu.Add(itemTypeSubmenu);

                // Obtenez la liste des items pour le type sélectionné
                List<ItemData> itemsOfType = ItemsList.Where(item => item.Type == itemType).ToList();

                // Créez un dictionnaire pour regrouper les items par SubType
                Dictionary<string, List<ItemData>> itemsBySubType = new Dictionary<string, List<ItemData>>();

                foreach (ItemData item in itemsOfType)
                {
                    if (!itemsBySubType.ContainsKey(item.SubType))
                    {
                        itemsBySubType[item.SubType] = new List<ItemData>();
                    }
                    itemsBySubType[item.SubType].Add(item);
                }

                // Parcourez chaque SubType et créez un sous-menu pour les items de ce SubType
                foreach (var subType in itemsBySubType.Keys)
                {
                    NativeMenu subTypeMenu = new NativeMenu(subType, subType, "SubType");
                    InstanceMain.pool.Add(subTypeMenu);

                    NativeSubmenuItem subTypeItem = new NativeSubmenuItem(subTypeMenu, itemTypeMenu);
                    itemTypeMenu.Add(subTypeItem);

                    // Parcourez les items de ce SubType
                    foreach (ItemData item in itemsBySubType[subType])
                    {
                        // Créez un nouveau menu pour afficher l'historique des prix pour cet item
                        NativeMenu itemHistoryMenu = new NativeMenu($"{item.Name} - Historique des prix", item.Name, InstanceMain.ConvertMoneyToString(item.Price).ToString() + "$");
                        InstanceMain.pool.Add(itemHistoryMenu);

                        NativeSubmenuItem itemDetail = new NativeSubmenuItem(itemHistoryMenu, subTypeMenu);
                        itemDetail.Description = $"Prix actuel: {InstanceMain.ConvertMoneyToString(item.Price)}";
                        subTypeMenu.Add(itemDetail);

                        // Parcourez l'historique des prix pour cet item
                        for (int i = item.PriceHistory.Count - 1; i >= 0; i--)
                        {
                            PriceHistoryEntry entry = item.PriceHistory[i];

                            string entryText = $"{InstanceMain.ConvertMoneyToString(entry.Price)} ({InstanceMain.ConvertMoneyToString(entry.Change)})";

                            // Ajoutez "Recent" au premier élément et "Oldest" au dernier
                            if (i == item.PriceHistory.Count - 1)
                            {
                                entryText += " (Recent)";
                            }
                            else if (i == 0)
                            {
                                entryText += " (Oldest)";
                            }

                            NativeItem historyEntry = new NativeItem(entryText);

                            // Appliquez la couleur en fonction de la fluctuation des prix
                            historyEntry.Colors.TitleNormal = entry.Change >= 0 ? Color.Green : Color.Red;

                            itemHistoryMenu.Add(historyEntry);
                        }
                    }
                }


            }

            // Affichez le menu
            stockExchangeMenu.Visible = true;
        }

        public void CreateMarketSellerMenu(MarketSeller marketSeller)
        {
            marketSellerMenu = new NativeMenu(marketSeller.Name);
            InstanceMain.pool.Add(marketSellerMenu);


            foreach (ItemData item in ItemsList)
            {
                if (item.SubType == marketSeller.ItemSubTypeSold)
                {
                    NativeItem nativeItem = new NativeItem(item.Name + " x1", InstanceMain.ConvertMoneyToString(item.Price / 2).ToString() + "$");
                    marketSellerMenu.Add(nativeItem);
                    nativeItem.Activated += (sender, selectedItem) => BuyItem(item, 1);

                    NativeItem nativeItem10 = new NativeItem(item.Name + " x10", InstanceMain.ConvertMoneyToString((item.Price * 10) / 2).ToString() + "$");
                    marketSellerMenu.Add(nativeItem10);
                    nativeItem10.Activated += (sender, selectedItem) => BuyItem(item, 10);

                    NativeItem nativeItem100 = new NativeItem(item.Name + " x100", InstanceMain.ConvertMoneyToString((item.Price * 100) / 2).ToString() + "$");
                    marketSellerMenu.Add(nativeItem100);
                    nativeItem100.Activated += (sender, selectedItem) => BuyItem(item, 100);
                }
            }
            marketSellerMenu.Visible = true;
        }

        public void CreateInventoryMenu()
        {
            inventoryMenu = new NativeMenu("Inventory");
            InstanceMain.pool.Add(inventoryMenu);

            foreach(InventoryItem items in InstanceMain.inventoryItems)
            {
                NativeItem nativeItem = new NativeItem(items.Item.Name, items.Item.SubType, items.Quantity.ToString());
                inventoryMenu.Add(nativeItem);

            }

            NativeMenu equipmentMenu = new NativeMenu("Equipments");
            InstanceMain.pool.Add(equipmentMenu);

            NativeSubmenuItem equipmentItem = new NativeSubmenuItem(equipmentMenu, inventoryMenu);
            inventoryMenu.Add(equipmentItem);

            inventoryMenu.Visible= true;
        }

        public void BuyItem(ItemData itemBought, int quantity)
        {
            if (actualWeight + (itemBought.Weight * quantity) < maxWeightOnPlayer)
            {
                if ((itemBought.Price * quantity) / 2 < InstanceMain.modMoney)
                {
                    InstanceMain.modMoney -= (itemBought.Price * quantity) / 2;
                    actualWeight += itemBought.Weight * quantity;
                    InventoryItem existingItem = InstanceMain.inventoryItems.FirstOrDefault(item => item.Item == itemBought);
                    if (existingItem != null)
                    {
                        existingItem.Quantity += quantity;
                    }
                    else
                    {
                        InstanceMain.inventoryItems.Add(new InventoryItem { Item = itemBought, Quantity = 1 });
                    }
                }
                else
                {
                    Notification.Show("Not enough money to buy this ! ");
                }
                InstanceMain.SaveAllData();
            } else
            {
                Notification.Show("You can't carry more item on you !");
            }
        }




        public void UpdateItemPrice(ItemData item, Numerics.BigInteger newPrice)
        {
            Numerics.BigInteger change = newPrice - item.Price;

            // Ajoutez une nouvelle entrée à l'historique des prix
            item.PriceHistory.Add(new PriceHistoryEntry { Price = newPrice, Change = change });

            // Supprimez la plus ancienne entrée si l'historique des prix dépasse 7 valeurs
            if (item.PriceHistory.Count > 7)
            {
                item.PriceHistory.RemoveAt(0);
            }

            // Mettez à jour le prix de l'item
            item.Price = newPrice;
            SaveItemsToJson();
        }

        public void UpdatePriceRandomly()
        {
            foreach (ItemData item in ItemsList)
            {
                Random random = new Random(GetUniqueSeed());

                // Génère un pourcentage de variation aléatoire entre -5% et 5%
                float priceVariationPercentage = (float)(random.NextDouble() * 20 - 10);

                if (CurrentMarketEvent != null && CurrentMarketEvent.AffectedSubtypes.Contains(item.SubType))
                {
                    priceVariationPercentage += CurrentMarketEvent.Impact;
                    CurrentMarketEvent = null;
                }
                // Convertit le pourcentage de variation en BigInteger
                Numerics.BigInteger percentageAsBigInteger = new Numerics.BigInteger((decimal)priceVariationPercentage);

                // Calcule le montant de la variation en fonction du pourcentage
                Numerics.BigInteger priceVariationAmount = (item.Price * percentageAsBigInteger) / 100;

                // Calcule le nouveau prix en ajoutant la variation
                Numerics.BigInteger newPrice = item.Price + priceVariationAmount;
                // Vérifie si le nouveau prix est en dessous de la limite inférieure, si oui, le fixe à la limite inférieure
                if (newPrice < item.LowerLimit)
                {
                    newPrice = item.LowerLimit;
                }
                // Vérifie si le nouveau prix est au-dessus de la limite supérieure, si oui, le fixe à la limite supérieure
                else if (newPrice > item.UpperLimit)
                {
                    newPrice = item.UpperLimit;
                }

                // Utilise la méthode existante pour mettre à jour le prix et l'historique des prix
                UpdateItemPrice(item, newPrice);
            }
        }
        private int GetUniqueSeed()
        {
            // Utilisez l'horodatage actuel pour générer une graine unique
            return (int)DateTime.Now.Ticks & 0x0000FFFF;
        }

        public void LoadCraftingRecipes(string filePath)
        {
            CraftingRecipes = new List<CraftingRecipe>();

            using (StreamReader sr = new StreamReader(filePath))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] values = line.Split(',');

                    CraftingRecipe recipe = new CraftingRecipe
                    {
                        ResultItemName = values[0],
                        Ingredients = new List<RecipeIngredient>()
                    };

                    for (int i = 1; i < values.Length; i += 2)
                    {
                        recipe.Ingredients.Add(new RecipeIngredient
                        {
                            ItemName = values[i],
                            Quantity = int.Parse(values[i + 1])
                        });
                    }

                    CraftingRecipes.Add(recipe);
                }
            }
        }

        public void LoadCraftingLocations(string filePath)
        {
            CraftingLocations = new List<CraftingLocation>();

            using (StreamReader sr = new StreamReader(filePath))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] values = line.Split(',');

                    CraftingLocation location = new CraftingLocation
                    {
                        Name = values[0],
                        Position = new Vector3(float.Parse(values[1]), float.Parse(values[2]), float.Parse(values[3])),
                        BlipSprite = int.Parse(values[4]),
                        Tier = int.Parse(values[5]),
                        AvailableRecipes = new List<string>()
                    };

                    for (int i = 6; i < values.Length; i++)
                    {
                        location.AvailableRecipes.Add(values[i]);
                    }

                    CraftingLocations.Add(location);
                }
            }
        }


        public void LoadMarketSellersFromCsv()
        {
            MarketSellers = new List<MarketSeller>();

            using (StreamReader sr = new StreamReader("scripts\\VTycoon\\marketsellers.csv"))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] values = line.Split(',');

                    MarketSeller marketSeller = new MarketSeller
                    {
                        Name = values[0],
                        Position = new Vector3(float.Parse(values[1]), float.Parse(values[2]), float.Parse(values[3])),
                        ItemSubTypeSold = values[4],
                        BlipSprite = int.Parse(values[5]),
                        Tier = int.Parse(values[6])
                    };

                    MarketSellers.Add(marketSeller);
                }
            }
        }

        public bool HasRequiredIngredients(CraftingRecipe recipe)
        {
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                InventoryItem itemInInventory = InstanceMain.inventoryItems.Find(item => item.Item.Name == ingredient.ItemName);

                if (itemInInventory == null || itemInInventory.Quantity < ingredient.Quantity)
                {
                    return false;
                }
            }
            return true;
        }


        public void CraftItem(CraftingRecipe recipe)
        {
            if (!HasRequiredIngredients(recipe))
            {
                // Le joueur n'a pas les ingrédients requis
                return;
            }

            // Retirer les ingrédients de l'inventaire du joueur
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                InventoryItem itemInInventory = InstanceMain.inventoryItems.Find(item => item.Item.Name == ingredient.ItemName);
                itemInInventory.Quantity -= ingredient.Quantity;
                actualWeight -= itemInInventory.Item.Weight * ingredient.Quantity;
            }

            // Ajouter l'objet résultant à l'inventaire du joueur
            InventoryItem resultItem = InstanceMain.inventoryItems.Find(item => item.Item.Name == recipe.ResultItemName);
            actualWeight += resultItem.Item.Weight;

            if (resultItem != null)
            {
                resultItem.Quantity += 1;
            }
            else
            {
                ItemData newItemData = ItemsList.Find(item => item.Name == recipe.ResultItemName);
                InstanceMain.inventoryItems.Add(new InventoryItem { Item = newItemData, Quantity = 1 });
            }

            InstanceMain.SaveAllData();
        }


        


        public void LoadMarketEventsFromCsv()
        {
            MarketEvents = new List<MarketEvent>();

            using (StreamReader sr = new StreamReader("scripts\\VTycoon\\marketevents.csv"))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] values = line.Split(',');

                    MarketEvent marketEvent = new MarketEvent
                    {
                        Name = values[0],
                        AffectedSubtypes = values[1].Split('|').ToList(),
                        Impact = float.Parse(values[2]),
                        NewsMessage = values[3]
                    };

                    MarketEvents.Add(marketEvent);
                }
            }
        }

        public void GenerateRandomMarketEvent()
        {
            Random random = new Random();
            int randomIndex = random.Next(MarketEvents.Count);
            CurrentMarketEvent = MarketEvents[randomIndex];
        }


        private List<ItemData> LoadItemsFromJson()
        {
            if (File.Exists(itemsJsonPath))
            {
                string json = File.ReadAllText(itemsJsonPath);
                return JsonConvert.DeserializeObject<List<ItemData>>(json);
            }
            else
            {
                GTA.UI.Notification.Show("Le fichier items.json est introuvable.");
                return new List<ItemData>();
            }


        }
        private void SaveItemsToJson()
        {
            string json = JsonConvert.SerializeObject(ItemsList, Formatting.Indented);
            File.WriteAllText(itemsJsonPath, json);
        }

        public class ItemData
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Type { get; set; }

            public string SubType { get; set; }
            public Numerics.BigInteger Price { get; set; }
            public float Weight { get; set; }

            public Numerics.BigInteger LowerLimit { get; set; }
            public Numerics.BigInteger UpperLimit { get; set; }

            public List<PriceHistoryEntry> PriceHistory { get; set; }
        }

        public class PriceHistoryEntry
        {
            public Numerics.BigInteger Change { get; set; }
            public Numerics.BigInteger Price { get; set; }

        }

        public class MarketEvent
        {
            public string Name { get; set; }
            public List<string> AffectedSubtypes { get; set; }
            public float Impact { get; set; }
            public string NewsMessage { get; set; }
        }

        public class MarketSeller
        {
            public string Name { get; set; }
            public Vector3 Position { get; set; }
            public string ItemSubTypeSold { get; set; }
            public int BlipSprite { get; set; }

            public int Tier { get ; set; }
        }

        public class InventoryItem
        {
            public ItemData Item { get; set; }
            public int Quantity { get; set; }
        }

        public class CraftingRecipe
        {
            public string ResultItemName { get; set; }
            public List<RecipeIngredient> Ingredients { get; set; }
        }

        public class RecipeIngredient
        {
            public string ItemName { get; set; }
            public int Quantity { get; set; }
        }

        public class CraftingLocation
        {
            public string Name { get; set; }
            public Vector3 Position { get; set; }
            public int BlipSprite { get; set; }

        public List<string> AvailableRecipes { get; set; }

        public int Tier { get ; set; }
        }



    }
}
