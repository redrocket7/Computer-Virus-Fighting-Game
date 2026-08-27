using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DungeonBatchVerify
{
    public static void Run()
    {
        int errors = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var generator = Object.FindAnyObjectByType<DungeonGenerator>();
        if (generator == null)
        {
            Debug.LogError("DungeonBatchVerify: SampleScene has no DungeonGenerator.");
            Exit(1);
            return;
        }

        generator.Generate();

        if (generator.PlacedRoomCount < 2)
        {
            Debug.LogError($"DungeonBatchVerify: expected a branching layout, got {generator.PlacedRoomCount} rooms.");
            errors++;
        }

        var rooms = generator.PlacedRooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null || room.Sockets == null)
            {
                Debug.LogError($"DungeonBatchVerify: room {i} is missing sockets.");
                errors++;
                continue;
            }

            int connected = 0;
            foreach (var socket in room.Sockets)
            {
                if (socket == null)
                {
                    Debug.LogError($"DungeonBatchVerify: room {i} has a null socket.");
                    errors++;
                    continue;
                }

                if (socket.IsConnected)
                {
                    connected++;
                    if (socket.Door != null && socket.Door.IsLocked)
                    {
                        Debug.LogError($"DungeonBatchVerify: connected socket {socket.name} started locked.");
                        errors++;
                    }
                }
                else if (socket.Door == null || !socket.Door.IsLocked)
                {
                    Debug.LogError($"DungeonBatchVerify: unused socket {socket.name} was not sealed.");
                    errors++;
                }
            }

            if (i > 0 && connected == 0)
            {
                Debug.LogError($"DungeonBatchVerify: room {i} is not connected to the dungeon.");
                errors++;
            }
        }

        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                if (rooms[i].GetWorldFootprint(-1f).Overlaps(rooms[j].GetWorldFootprint(-1f)))
                {
                    Debug.LogError($"DungeonBatchVerify: rooms {i} and {j} overlap.");
                    errors++;
                }
            }
        }

        if (errors > 0)
        {
            Debug.LogError($"DungeonBatchVerify failed with {errors} error(s).");
            Exit(1);
            return;
        }

        Debug.Log($"DungeonBatchVerify passed with {generator.PlacedRoomCount} rooms.");
        Exit(0);
    }

    static void Exit(int code)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }
}
