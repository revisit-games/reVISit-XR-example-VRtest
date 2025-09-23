using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class SaveManager
{
    // Define the save folder path
    public static readonly string SAVE_FOLDER = Application.dataPath + "/Saves/";

    // If the save folder does not exist, create it
    public static void Init()
    {
        // Test if save folder exists
        if (!Directory.Exists(SAVE_FOLDER))
        {
            // Create save folder
            Directory.CreateDirectory(SAVE_FOLDER);
        }
    }

    // Give names to the save files and save them in the save folder
    public static void Save(string saveString)
    {
        int saveNumber = 1;
        while (File.Exists(SAVE_FOLDER + "save_" + saveNumber + ".json"))
        {
            saveNumber++;
        }
        File.WriteAllText(SAVE_FOLDER + "save_" + saveNumber + ".json", saveString);
    }
}
