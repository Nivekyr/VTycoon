using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using LemonUI.Menus;
using Newtonsoft.Json;
using static VTycoon.ItemManager;
using static VTycoon.VehicleManager;

namespace VTycoon
{
    public class VehicleManager : Script
    {
        private List<Location> locations = new List<Location>();


        // Dictionnaire des véhicules achetés par le joueur
        private List<VehicleData> playerVehicles;


        // Fichier CSV pour les garages
        private string locationsFilePath = "scripts\\VTycoon\\locations.csv";
        private string playerDataFilePath = "scripts\\VTycoon\\playerData.json";

        private Main mainScript;

        private NativeMenu vehicleMenu;
        private NativeMenu garageMenu;
        private NativeMenu lsCustomMenu;
        private NativeMenu insuranceMenu;
        private NativeMenu inventoryVehicleMenu;

        private List<InventoryItem> actualInventoryVehicle;

        public VehicleManager()
        {
            mainScript = Main.InstanceMain;
            LoadPlayerData();
            LoadLocationsFromFile(locationsFilePath);
            CreateBlipsForVehicleManager();
            Tick += OnTick;
            Interval = 1;
        }

        private List<VehicleData> ReadVehicleData(string filePath)
        {
            string jsonData = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<VehicleData>>(jsonData);
        }

        private void OnTick(object sender, EventArgs e)
        {


            // Vérifiez si le joueur est près d'un garage
            foreach (Location location in locations)
            {
                OnPlayerNearLocation(location);
            }

            

            if (Game.Player.Character.IsInVehicle())
            {
                Vehicle playerVehicle = Game.Player.Character.CurrentVehicle;

                
                // Trouvez le VehicleData correspondant au véhicule actuel du joueur
                VehicleData vehicleData = playerVehicles.FirstOrDefault(v => v.ModelName == playerVehicle.Model.Hash.ToString());
                if (IsVehicleOwnedByPlayer(playerVehicle))
                {
                    if (Game.IsControlJustPressed(Control.Sprint))
                    {
                        CreateInventoryVehicleMenu();
                    }
                }
                // Vérifiez si le véhicule appartient au joueur et s'il est en cours d'utilisation
                if (vehicleData != null && vehicleData.InUse)
                {
                    // Créez un blip pour le véhicule s'il n'en a pas déjà un
                    if (playerVehicle.AttachedBlip == null)
                    {
                        Blip vehicleBlip = playerVehicle.AddBlip();
                        vehicleBlip.Sprite = BlipSprite.PersonalVehicleCar;
                        vehicleBlip.Name = "Véhicule personnel";
                        vehicleBlip.IsShortRange = true;
                        vehicleBlip.Color = BlipColor.Blue;
                    }
                }
                else if (playerVehicle.AttachedBlip != null)
                {
                    // Supprimez le blip si le véhicule n'est plus en cours d'utilisation
                    playerVehicle.AttachedBlip.Delete();
                }
            }

        }

        private void LoadLocationsFromFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] data = line.Split(',');
                        Vector3 position = new Vector3(
                            float.Parse(data[0]),
                            float.Parse(data[1]),
                            float.Parse(data[2]));
                        string type = data[3];
                        Location location = new Location { Position = position, Type = type };
                        locations.Add(location);
                    }
                }
            }
            else
            {
                // Fichier non trouvé, vous pouvez afficher un message d'erreur ou créer des lieux par défaut
                Notification.Show("The file for locations hasnt been found");
            }
        }




        private void BuyVehicle(string vehicleName, int vehicleCost)
        {

            if (mainScript.modMoney >= vehicleCost)
            {
                mainScript.modMoney -= vehicleCost;
                Vector3 spawnPosition = new Vector3(-48.53f, -1113.10f, 26.44f);
                Vehicle vehicle = World.CreateVehicle(vehicleName, spawnPosition, 77.68f);
                vehicle.PlaceOnGround();
                Game.Player.Character.Task.WarpIntoVehicle(vehicle, VehicleSeat.Driver);

                // Créez un nouvel objet VehicleData
                VehicleData newVehicleData = new VehicleData { ModelName = vehicle.Model.Hash.ToString(), Handle = vehicle.Handle };
                newVehicleData.InUse = true;
                newVehicleData.Mods = SaveVehicleMods(vehicle, newVehicleData); // Ajoutez cette ligne
                newVehicleData.Storage = GetStorageForVehicle(vehicle);
                newVehicleData.VehicleInventory = new List<InventoryItem>();
                newVehicleData.Price = vehicleCost;

                // Ajoutez le véhicule à la liste des véhicules du joueur
                playerVehicles.Add(newVehicleData);
                SavePlayerData();

            }
            else
            {
                GTA.UI.Notification.Show($"Vous n'avez pas assez d'argent pour acheter ce véhicule. Argent actuel : {mainScript.modMoney}");
            }
        }

        private float GetStorageForVehicle(Vehicle vehicle)
        {
            float storageDefault;
            switch(vehicle.ClassType)
            {
                case VehicleClass.Compacts:
                    storageDefault = 125;
                    break;
                case VehicleClass.Sedans:
                    storageDefault = 250;
                    break;
                case VehicleClass.SUVs:
                    storageDefault = 300;
                    break;
                case VehicleClass.Coupes:
                    storageDefault = 150;
                    break;
                case VehicleClass.Muscle:
                    storageDefault = 200;
                    break;
                case VehicleClass.Sports:
                    storageDefault = 100;
                    break;
                case VehicleClass.Super:
                    storageDefault = 75;
                    break;
                case VehicleClass.Motorcycles:
                    storageDefault = 50;
                    break;
                case VehicleClass.OffRoad:
                    storageDefault = 400;
                    break;
                case VehicleClass.Commercial:
                    storageDefault = 1000;
                    break;
                case VehicleClass.SportsClassics:
                    storageDefault = 100;
                    break;
                case VehicleClass.OpenWheel:
                    storageDefault = 25;
                    break;
                case VehicleClass.Service:
                    storageDefault = 750;
                    break;
                default:
                    storageDefault = 0;
                    break;

            }

            return storageDefault;
        }
        private void ShowVehicleMenu()
        {

            vehicleMenu = new NativeMenu("Concessionnaire", "Acheter un véhicule");
            mainScript.pool.Add(vehicleMenu);

            // Lisez les données du fichier JSON
            List<VehicleData> vehicleDataList = ReadVehicleData("scripts\\VTycoon\\vehicles.json");

            // Groupez les véhicules par catégorie
            var vehicleCategories = vehicleDataList.GroupBy(v => v.Category);

            // Créez des sous-menus pour chaque catégorie et ajoutez les véhicules
            foreach (var categoryGroup in vehicleCategories)
            {
                string categoryName = categoryGroup.Key;

                // Créez un menu pour la catégorie actuelle
                NativeMenu categoryMenu = new NativeMenu("Concessionnaire", categoryName);
                mainScript.pool.Add(categoryMenu);

                // Ajoutez un élément de sous-menu pour la catégorie actuelle dans le menu principal
                NativeSubmenuItem categoryItem = new NativeSubmenuItem(categoryMenu, vehicleMenu);
                vehicleMenu.Add(categoryItem);

                // Ajoutez les véhicules pour cette catégorie
                foreach (VehicleData vehicleData in categoryGroup)
                {
                    string vehicleDisplayName = vehicleData.ModelName;
                    NativeItem vehicleItem = new NativeItem(vehicleDisplayName, mainScript.ConvertMoneyToString(vehicleData.Price) + "$");
                    categoryMenu.Add(vehicleItem);

                    // Ajoutez des événements pour les véhicules
                    vehicleItem.Activated += (sender, args) => BuyVehicle(vehicleData.ModelName, vehicleData.Price);
                }
            }

            // Affichez le menu
            vehicleMenu.Visible = true;
        }
        private void ShowGarageMenu()
        {
            garageMenu = new NativeMenu("Garage", "Vos véhicules");

            mainScript.pool.Add(garageMenu);

            // Parcourez les véhicules appartenant au joueur
            foreach (VehicleData vehicleData in playerVehicles)
            {
                string vehicleDisplayName = Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, int.Parse(vehicleData.ModelName));

                // Créez un élément de menu pour chaque véhicule
                NativeItem vehicleItem = new NativeItem(vehicleDisplayName);
                garageMenu.Add(vehicleItem);

                // Ajoutez un événement pour récupérer le véhicule
                if (!vehicleData.InUse)
                {
                    vehicleItem.Activated += (sender, args) =>
                    {
                        // Créez le véhicule dans le monde
                        Vehicle vehicle = World.CreateVehicle(int.Parse(vehicleData.ModelName), Game.Player.Character.Position + Game.Player.Character.ForwardVector * 5);
                        vehicle.PlaceOnGround();
                        Game.Player.Character.Task.WarpIntoVehicle(vehicle, VehicleSeat.Driver);
                        ApplyVehicleMods(vehicle, vehicleData.Mods, vehicleData);
                        vehicleData.Handle = vehicle.Handle;

                        garageMenu.Visible = false;
                        garageMenu = null;

                        // Mettez à jour l'état du véhicule pour indiquer qu'il est en cours d'utilisation par le joueur
                        vehicleData.InUse = true;
                        SavePlayerData();
                    };
                } else
                {
                    Notification.Show("Ce véhicule est déjà sorti !");
                }

            }

            // Affichez le menu
            garageMenu.Visible = true;
        }

        private void ShowLsCustomMenu()
        {
            lsCustomMenu = new NativeMenu("LsCustom", "Modifiez votre véhicule");
            mainScript.pool.Add(lsCustomMenu);

            Vehicle vehicle = Game.Player.Character.CurrentVehicle;

            if (vehicle == null)
            {
                Notification.Show("Vous devez être dans un véhicule pour utiliser LsCustom.");
                return;
            }

            foreach (VehicleModType modType in Enum.GetValues(typeof(VehicleModType)))
            {
                string categoryName = modType.ToString();
                NativeMenu categoryMenu = new NativeMenu("Concessionnaire", categoryName);
                mainScript.pool.Add(categoryMenu);

                // Ajoutez un élément de sous-menu pour la catégorie actuelle dans le menu principal
                NativeSubmenuItem categoryItem = new NativeSubmenuItem(categoryMenu, lsCustomMenu);
                lsCustomMenu.Add(categoryItem);

                int modCount = vehicle.Mods[modType].Count;
                for (int i = -1; i < modCount; i++)
                {
                    // Calculez le prix en fonction du type de modification et de l'index
                    int price;
                    if (modType == VehicleModType.Engine || modType == VehicleModType.Brakes || modType == VehicleModType.Transmission || modType == VehicleModType.Suspension)
                    {
                        if (i == -1)
                        {
                            price = 0;
                        }
                        else
                        {
                            price = 10000 * (int)Math.Pow(2, i);
                        }
                    }
                    else
                    {
                        if (i == -1)
                        {
                            price = 0;
                        }
                        else
                        {
                            price = 2500;
                        }
                    }

                    string optionName = i == -1 ? "Par défaut" : $"Option {i + 1}";
                    string optionDescription = i == -1 ? $"Rétablir l'option par défaut pour {modType.ToString().ToLower()}." : $"Appliquez l'option {i + 1} pour {modType.ToString().ToLower()}.";

                    NativeItem modItem = new NativeItem($"{optionName} - ${price}", optionDescription);
                    categoryMenu.Add(modItem);

                    int modIndex = i;
                    modItem.Activated += (sender, args) =>
                    {
                        // Vérifiez si le joueur a suffisamment d'argent
                        if (mainScript.modMoney >= price)
                        {
                            ApplyMod(vehicle, modType, modIndex);

                            // Déduisez le prix de l'argent du joueur
                            mainScript.modMoney -= price;
                            SavePlayerData();
                        }
                        else
                        {
                            Notification.Show("Vous n'avez pas assez d'argent pour cette modification.");
                        }
                    };
                }
            }

            NativeMenu colorsMenu = new NativeMenu("Couleurs", "Modifier les couleurs");
            mainScript.pool.Add(colorsMenu);

            // Ajoutez un élément de sous-menu pour la catégorie des couleurs dans le menu principal
            NativeSubmenuItem colorsItem = new NativeSubmenuItem(colorsMenu, lsCustomMenu);
            lsCustomMenu.Add(colorsItem);
            VehicleData vehicleData = playerVehicles.FirstOrDefault(v => v.Handle == vehicle.Handle);

            NativeItem storageUpgradeItem = new NativeItem("Upgrade Storage", $"Capacity : {vehicleData.Storage}");
            lsCustomMenu.Add(storageUpgradeItem);
            storageUpgradeItem.Activated += (sender, args) =>
            {

                int price = vehicleData.Price / 5;
                if (mainScript.modMoney > price)
                {
                    mainScript.modMoney -= price;
                    vehicleData.Storage += 10f;
                }
                else
                {
                    Notification.Show("Not enough money to upgrade the storage");
                }
            };

            // Créez des sliders pour les composants RGB de chaque couleur
            NativeSliderItem primaryRed = new NativeSliderItem($"Rouge (Primary): {vehicle.Mods.CustomPrimaryColor.R}", 255, 0);
            NativeSliderItem primaryGreen = new NativeSliderItem($"Vert (Primary): {vehicle.Mods.CustomPrimaryColor.G}", 255, 0);
            NativeSliderItem primaryBlue = new NativeSliderItem($"Bleu (Primary): {vehicle.Mods.CustomPrimaryColor.B}", 255, 0);
            colorsMenu.Add(primaryRed);
            colorsMenu.Add(primaryGreen);
            colorsMenu.Add(primaryBlue);

            NativeSliderItem secondaryRed = new NativeSliderItem($"Rouge (Secondary): {vehicle.Mods.CustomSecondaryColor.R}", 255, 0);
            NativeSliderItem secondaryGreen = new NativeSliderItem($"Vert (Secondary): {vehicle.Mods.CustomSecondaryColor.G}", 255, 0);
            NativeSliderItem secondaryBlue = new NativeSliderItem($"Bleu (Secondary): {vehicle.Mods.CustomSecondaryColor.B}", 255, 0);
            colorsMenu.Add(secondaryRed);
            colorsMenu.Add(secondaryGreen);
            colorsMenu.Add(secondaryBlue);



            // Mettez à jour les couleurs du véhicule en temps réel
            primaryRed.ValueChanged += (sender, value) =>
            {
                primaryRed.Title = $"Rouge (Primary): {primaryRed.Value}";
                vehicle.Mods.CustomPrimaryColor = Color.FromArgb(primaryRed.Value, vehicle.Mods.CustomPrimaryColor.G, vehicle.Mods.CustomPrimaryColor.B);
            };

            primaryGreen.ValueChanged += (sender, value) =>
            {
                primaryGreen.Title = $"Vert (Primary): {primaryGreen.Value}";
                vehicle.Mods.CustomPrimaryColor = Color.FromArgb(vehicle.Mods.CustomPrimaryColor.R, primaryGreen.Value, vehicle.Mods.CustomPrimaryColor.B);
            };

            primaryBlue.ValueChanged += (sender, value) =>
            {
                primaryBlue.Title = $"Bleu (Primary): {primaryBlue.Value}";
                vehicle.Mods.CustomPrimaryColor = Color.FromArgb(vehicle.Mods.CustomPrimaryColor.R, vehicle.Mods.CustomPrimaryColor.G, primaryBlue.Value);
            };

            secondaryRed.ValueChanged += (sender, value) =>
            {
                secondaryRed.Title = $"Rouge (Secondary): {secondaryRed.Value}";
                vehicle.Mods.CustomSecondaryColor = Color.FromArgb(secondaryRed.Value, vehicle.Mods.CustomSecondaryColor.G, vehicle.Mods.CustomSecondaryColor.B);
            };

            secondaryGreen.ValueChanged += (sender, value) =>
            {
                secondaryGreen.Title = $"Vert (Secondary): {secondaryGreen.Value}";
                vehicle.Mods.CustomSecondaryColor = Color.FromArgb(vehicle.Mods.CustomSecondaryColor.R, secondaryGreen.Value, vehicle.Mods.CustomSecondaryColor.B);
            };

            secondaryBlue.ValueChanged += (sender, value) =>
            {
                secondaryBlue.Title = $"Bleu (Secondary): {secondaryBlue}";
                vehicle.Mods.CustomSecondaryColor = Color.FromArgb(vehicle.Mods.CustomSecondaryColor.R, vehicle.Mods.CustomSecondaryColor.G, secondaryBlue.Value);
            };

            lsCustomMenu.Visible = true;
        }

        public void CreateInventoryVehicleMenu()
        {
            Vehicle playerVehicle = Game.Player.Character.CurrentVehicle;
            VehicleData vehicleData = playerVehicles.FirstOrDefault(v => v.ModelName == playerVehicle.Model.Hash.ToString());
            actualInventoryVehicle = vehicleData.VehicleInventory;
            inventoryVehicleMenu = new NativeMenu("Inventory");
            mainScript.pool.Add(inventoryVehicleMenu);

            // Sous-menu pour retirer des items du véhicule
            NativeMenu withdrawVehicleInventoryMenu = new NativeMenu("From Vehicle", "Take item from vehicle");
            NativeSubmenuItem withdrawSubmenu = inventoryVehicleMenu.AddSubMenu(withdrawVehicleInventoryMenu);
            mainScript.pool.Add(withdrawVehicleInventoryMenu);

            foreach (var item in actualInventoryVehicle)
            {
                NativeItem nativeItem = new NativeItem(item.Item.Name, item.Item.SubType, item.Quantity.ToString());
                withdrawVehicleInventoryMenu.Add(nativeItem);
                nativeItem.Activated += (sender, selectedItem) => TakeItemFromVehicle(item, actualInventoryVehicle, 1);
                NativeItem itemall = new NativeItem(item.Item.Name + " xAll", item.Quantity.ToString());
                withdrawVehicleInventoryMenu.Add(itemall);
                itemall.Activated += (sender, selectedItem) => TakeItemFromVehicle(item, actualInventoryVehicle, item.Quantity);
            }

            // Sous-menu pour déposer des items dans le véhicule
            NativeMenu depositPlayerInventoryMenu = new NativeMenu("From Player", "Deposit Item");
            NativeSubmenuItem depositSubmenu = inventoryVehicleMenu.AddSubMenu(depositPlayerInventoryMenu);
            mainScript.pool.Add(depositPlayerInventoryMenu);

            foreach (var inventoryItem in mainScript.inventoryItems)
            {
                NativeItem item = new NativeItem(inventoryItem.Item.Name + " x1", inventoryItem.Quantity.ToString());
                depositPlayerInventoryMenu.Add(item);
                item.Activated += (sender, selectedItem) => DepositItemInVehicle(inventoryItem, actualInventoryVehicle, 1);
                NativeItem itemall = new NativeItem(inventoryItem.Item.Name + " xAll", inventoryItem.Quantity.ToString());
                depositPlayerInventoryMenu.Add(itemall);
                itemall.Activated += (sender, selectedItem) => DepositItemInVehicle(inventoryItem, actualInventoryVehicle, inventoryItem.Quantity);
            }

            inventoryVehicleMenu.Visible = true;
        }

        private void DepositItemInVehicle(InventoryItem item, List<InventoryItem> vehicleInventory, int quantity)
        {
            Vehicle vehicle = Game.Player.Character.CurrentVehicle;
            VehicleData vehicleData = playerVehicles.FirstOrDefault(v => v.Handle == vehicle.Handle);

            if (item.Item.Weight * quantity < vehicleData.Storage)
            {
                // Vérifiez si l'item existe déjà dans l'inventaire du véhicule
                var existingItemInVehicle = vehicleInventory.FirstOrDefault(i => i.Item.Name == item.Item.Name);

                if (existingItemInVehicle != null)
                {
                    // Si l'item existe déjà, augmentez simplement la quantité
                    existingItemInVehicle.Quantity += quantity;
                }
                else
                {
                    // Si l'item n'existe pas, ajoutez un nouvel item à l'inventaire du véhicule
                    vehicleInventory.Add(new InventoryItem
                    {
                        Item = item.Item,
                        Quantity = quantity
                    });
                }
                if (item.Quantity == 0)
                {
                    mainScript.inventoryItems.Remove(item);
                }

                itemManagerInstance.actualWeight -= item.Item.Weight * quantity;
                vehicleData.Storage += item.Item.Weight * quantity;
                // Retirez la quantité déposée de l'inventaire du joueur
                item.Quantity -= quantity;
                SavePlayerData();
            }else
            {
                Notification.Show("Not enough space to deposit");
            }
        }

        private void TakeItemFromVehicle(InventoryItem item, List<InventoryItem> vehicleInventory, int quantity)
        {
            Vehicle vehicle = Game.Player.Character.CurrentVehicle;
            VehicleData vehicleData = playerVehicles.FirstOrDefault(v => v.Handle == vehicle.Handle);
            // Vérifiez si l'item existe dans l'inventaire du véhicule
            var existingItemInVehicle = vehicleInventory.FirstOrDefault(i => i.Item.Name == item.Item.Name);

            if (existingItemInVehicle != null)
            {
                if (itemManagerInstance.actualWeight + (existingItemInVehicle.Item.Weight * quantity) > itemManagerInstance.maxWeightOnPlayer)
                {
                    Notification.Show("No space available on player");
                    return;
                }

                vehicleData.Storage -= item.Item.Weight * quantity;
                itemManagerInstance.actualWeight += item.Item.Weight * quantity;
                // Si l'item existe, réduisez la quantité
                existingItemInVehicle.Quantity -= quantity;

                // Si la quantité de l'item dans le véhicule atteint 0, retirez l'item de l'inventaire du véhicule
                if (existingItemInVehicle.Quantity <= 0)
                {
                    vehicleInventory.Remove(existingItemInVehicle);
                }

                // Ajoutez la quantité retirée à l'inventaire du joueur
                var existingItemInPlayerInventory = mainScript.inventoryItems.FirstOrDefault(i => i.Item.Name == item.Item.Name);
                if (existingItemInPlayerInventory != null)
                {
                    existingItemInPlayerInventory.Quantity += quantity;
                }
                else
                {
                    // Si l'item n'existe pas dans l'inventaire du joueur, ajoutez-le
                    mainScript.inventoryItems.Add(new InventoryItem
                    {
                        Item = item.Item,
                        Quantity = quantity
                    });
                }
            }
            SavePlayerData();
        }




        private void ShowInsuranceMenu()
        {
            insuranceMenu = new NativeMenu("Assurance", "Récupérer un véhicule détruit");

            mainScript.pool.Add(insuranceMenu);

            foreach (VehicleData vehicleData in playerVehicles)
            {
                if (vehicleData.IsDestroyed)
                {
                    string vehicleDisplayName = Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, int.Parse(vehicleData.ModelName));
                    NativeItem vehicleItem = new NativeItem(vehicleDisplayName);
                    insuranceMenu.Add(vehicleItem);

                    vehicleItem.Activated += (sender, args) =>
                    {
                        // Modifiez cette valeur pour déterminer le coût de récupération du véhicule
                        int recoveryCost = vehicleData.Price / 10;

                        if (mainScript.modMoney >= recoveryCost)
                        {
                            mainScript.modMoney -= recoveryCost;
                            vehicleData.IsDestroyed = false;
                            SavePlayerData();
                            Notification.Show($"Vous avez récupéré votre {vehicleDisplayName} pour ${recoveryCost}. Le véhicule est disponible à votre garage !");
                            insuranceMenu.Visible = false;
                            insuranceMenu = null;
                        }
                        else
                        {
                            Notification.Show("Vous n'avez pas assez d'argent pour récupérer ce véhicule.");
                        }
                    };
                }
            }

            // Affichez le menu
            insuranceMenu.Visible = true;
        }


        private void ApplyMod(Vehicle vehicle, VehicleModType modType, int index)
        {
            vehicle.Mods.InstallModKit();
            vehicle.Mods[modType].Index = index;
            SavePlayerData();
            Notification.Show($"Modification {modType} appliquée avec l'index {index}");
        }

        private bool IsVehicleOwnedByPlayer(Vehicle vehicle)
        {
            return playerVehicles.Any(v => v.Handle == vehicle.Handle && v.ModelName == vehicle.Model.Hash.ToString() && v.InUse);
        }

        private void CreateBlipsForVehicleManager()
        {
            foreach (Location location in locations)
            {
                Blip blip = World.CreateBlip(location.Position);

                switch (location.Type)
                {
                    case "concessionnaire":
                        blip.Sprite = BlipSprite.CarWash;
                        blip.Color = BlipColor.NetPlayer25;
                        blip.Name = "Concessionnaire";
                        break;
                    case "garage":
                        blip.Sprite = BlipSprite.Garage;
                        blip.Color = BlipColor.NetPlayer25;
                        blip.Name = "Garage";
                        break;
                    case "lscustom":
                        blip.Sprite = BlipSprite.Bennys;
                        blip.Color = BlipColor.NetPlayer25;
                        blip.Name = "LsCustom";
                        break;
                    case "insurance":
                        blip.Sprite = (BlipSprite)811;
                        blip.Color = BlipColor.NetPlayer25;
                        blip.Name = "Insurance1";
                        break;
                    default:
                        break;
                }
            }
        }



        private void OnPlayerNearLocation(Location location)
        {
            Vehicle currentVehicle = Game.Player.Character.CurrentVehicle;

            if (Game.Player.Character.Position.DistanceTo(location.Position) < 5.0f)
            {
                switch (location.Type)
                {
                    case "garage":
                        if (currentVehicle == null)
                        {
                            if (garageMenu == null)
                            {
                                GTA.UI.Notification.Show("Appuyez sur E pour ouvrir le garage et choisir un véhicule à sortir");
                            }
                            if (Game.IsControlJustPressed(GTA.Control.Context))
                            {
                                // Affichez le menu du garage
                                ShowGarageMenu();
                            }
                        }
                        else
                        {
                            if (IsVehicleOwnedByPlayer(currentVehicle))
                            {
                                GTA.UI.Notification.Show("Appuyez sur E pour stocker votre véhicule dans le garage.");

                                if (Game.IsControlJustPressed(GTA.Control.Context))
                                {
                                    // Ajoutez le véhicule à la liste des véhicules stockés dans le garage
                                    VehicleData vehicleData = playerVehicles.FirstOrDefault(v => v.Handle == currentVehicle.Handle);

                                    if (vehicleData != null)
                                    {
                                        vehicleData.Mods = SaveVehicleMods(currentVehicle, vehicleData); // Ajoutez cette ligne
                                        vehicleData.InUse = false;
                                        SavePlayerData();
                                        Notification.Show("Véhicule ajouté au garage");

                                        // Supprimez le véhicule du monde
                                        currentVehicle.Delete();
                                    }
                                    else
                                    {
                                        Notification.Show("Une erreur s'est produite. Le véhicule n'a pas été ajouté au garage.");
                                    }
                                }
                            }
                            else
                            {
                                Notification.Show("Vous ne pouvez pas stocker ce véhicule car il ne vous appartient pas.");
                            }
                        }
                        break;
                    case "concessionnaire":
                        if (vehicleMenu == null)
                        {
                            GTA.UI.Notification.Show("Appuyez sur E pour acheter un véhicule.");
                        }

                        if (Game.IsControlJustPressed(GTA.Control.Context))
                        {
                            // Affichez le menu du concessionnaire
                            ShowVehicleMenu();
                        }
                        break;
                    case "lscustom":
                        if (IsVehicleOwnedByPlayer(currentVehicle))
                        {
                            if (lsCustomMenu == null)
                            {
                                GTA.UI.Notification.Show("Appuyez sur E pour modifier ce véhicule.");
                            }

                            if (Game.IsControlJustPressed(GTA.Control.Context))
                            {
                                // Affichez le menu du concessionnaire
                                ShowLsCustomMenu();
                            }
                        }
                        break;
                    case "insurance":
                        if (insuranceMenu == null)
                        {
                            GTA.UI.Notification.Show("Appuyez sur E pour modifier ce véhicule.");
                        }

                        if (Game.IsControlJustPressed(GTA.Control.Context))
                        {
                            // Affichez le menu du concessionnaire
                            ShowInsuranceMenu();
                        }
                        break;
                    default:
                        break;
                }
            }
        }


        private void SavePlayerData()
        {
            // Chargez les données actuelles du joueur
            PlayerData currentPlayerData = mainScript.LoadPlayerData(playerDataFilePath);

            // Mettez à jour les véhicules du joueur
            currentPlayerData.StoredVehicles = playerVehicles;


            // Enregistrez les données du joueur mises à jour
            mainScript.SavePlayerData(currentPlayerData, playerDataFilePath);
        }

        private void LoadPlayerData()
        {
            PlayerData currentPlayerData = mainScript.LoadPlayerData(playerDataFilePath);

            playerVehicles = currentPlayerData.StoredVehicles;
        }

        private Dictionary<VehicleModType, int> SaveVehicleMods(Vehicle vehicle, VehicleData vehicleData)
        {
            Dictionary<VehicleModType, int> mods = new Dictionary<VehicleModType, int>();

            vehicle.Mods.InstallModKit();

            foreach (VehicleModType modType in Enum.GetValues(typeof(VehicleModType)))
            {
                int modIndex = vehicle.Mods[modType].Index;
                if (modIndex != -1)
                {
                    mods.Add(modType, modIndex);
                }
            }

            // Ajoutez les couleurs primaires et secondaires personnalisées du véhicule
            vehicleData.PrimaryColor = vehicle.Mods.CustomPrimaryColor.ToArgb();
            vehicleData.SecondaryColor = vehicle.Mods.CustomSecondaryColor.ToArgb();
            vehicleData.PearlescentColor = vehicle.Mods.PearlescentColor;


            return mods;
        }


        private void ApplyVehicleMods(Vehicle vehicle, Dictionary<VehicleModType, int> mods, VehicleData vehicleData)
        {
            vehicle.Mods.InstallModKit();

            if (mods == null)
            {
                Notification.Show("Aucune modification à appliquer.");
                return;
            }

            foreach (KeyValuePair<VehicleModType, int> entry in mods)
            {
                vehicle.Mods[entry.Key].Index = entry.Value;
            }

            // Appliquez les couleurs primaires et secondaires personnalisées du véhicule
            vehicle.Mods.CustomPrimaryColor = Color.FromArgb(vehicleData.PrimaryColor);
            vehicle.Mods.CustomSecondaryColor = Color.FromArgb(vehicleData.SecondaryColor);
            vehicle.Mods.PearlescentColor = vehicleData.PearlescentColor;
            
        }



        public class Location
        {
            public Vector3 Position { get; set; }
            public string Type { get; set; }
        }

        public class VehicleData
        {
            public string ModelName { get; set; }
            public string Category { get; set; }
            public int Handle { get; set; }
            public int Price { get; set; }
            public bool InUse { get; set; }

            public bool IsDestroyed { get; set; }
            public float Storage { get; set; }

            public List<InventoryItem> VehicleInventory;


            public Dictionary<VehicleModType, int> Mods { get; set; } = new Dictionary<VehicleModType, int>();

            public int PrimaryColor { get; set; }
            public int SecondaryColor { get; set; }
            public VehicleColor PearlescentColor { get; set; }

        }




    }




}
