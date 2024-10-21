using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static readonly string BotToken = "botid";
    private static ITelegramBotClient botClient;

    // Словарь для хранения состояния игр каждого пользователя
    private static Dictionary<long, GameState> userGames = new Dictionary<long, GameState>();

    // Игра "2048" использует 4х4 игровое поле
    private static readonly int BoardSize = 4;

    static async Task Main(string[] args)
    {
        botClient = new TelegramBotClient(BotToken);

        var cts = new System.Threading.CancellationTokenSource();
        var cancellationToken = cts.Token;

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cancellationToken);

        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Бот {me.Username} запущен");

        Console.ReadLine();
        cts.Cancel();
    }

    // Обработка входящих сообщений
    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, System.Threading.CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Text)
        {
            var message = update.Message;
            var userId = message.From.Id;

            // Вызов основного меню
            if (message.Text == "/start")
            {
                await ShowMainMenu(message.Chat.Id);
            }
            else if (message.Text == "Угадай число")
            {
                await StartGuessGame(message.Chat.Id, userId);
            }
            else if (message.Text == "Игра 2048")
            {
                await StartGame2048(message.Chat.Id, userId);
            }
            else if (userGames.ContainsKey(userId))
            {
                if (userGames[userId].CurrentGame == "guess")
                {
                    await ProcessGuessAsync(message);
                }
                else if (userGames[userId].CurrentGame == "2048")
                {
                    await Handle2048Input(message);
                }
            }
        }
    }

    // Главное меню с текстовым выбором игр
    private static async Task ShowMainMenu(long chatId)
    {
        await botClient.SendTextMessageAsync(chatId, "Выберите игру: \n1. Угадай число\n2. Игра 2048", replyMarkup: new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Угадай число", "Игра 2048" }
        })
        {
            ResizeKeyboard = true
        });
    }

    // Начало игры "Угадай число"
    private static async Task StartGuessGame(long chatId, long userId)
    {
        Random rand = new Random();
        int number = rand.Next(1, 101);

        userGames[userId] = new GameState
        {
            CurrentGame = "guess",
            SecretNumber = number
        };

        await botClient.SendTextMessageAsync(chatId, "Я загадал число от 1 до 100. Угадай его!");
    }

    // Обработка попыток угадать число
    private static async Task ProcessGuessAsync(Message message)
    {
        var userId = message.From.Id;

        if (!userGames.ContainsKey(userId))
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Ты еще не начал игру! Введи команду /start, чтобы начать.");
            return;
        }

        int guessedNumber;
        if (!int.TryParse(message.Text, out guessedNumber))
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Пожалуйста, введи число.");
            return;
        }

        int actualNumber = userGames[userId].SecretNumber;

        if (guessedNumber < actualNumber)
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Загаданное число больше.");
        }
        else if (guessedNumber > actualNumber)
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Загаданное число меньше.");
        }
        else
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Поздравляю! Ты угадал число.");
            userGames.Remove(userId);
            await ShowMainMenu(message.Chat.Id); // Вернуться в главное меню
        }
    }

    private static async Task StartGame2048(long chatId, long userId)
    {
        int[,] board = new int[BoardSize, BoardSize];
        AddRandomTile(board);
        AddRandomTile(board);

        userGames[userId] = new GameState
        {
            CurrentGame = "2048",
            Board = board
        };

        // Отправляем сообщение с кнопками для управления игрой
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
        new KeyboardButton[] { "⬆️", "⬅️" },  // Кнопки вверх и влево
        new KeyboardButton[] { "⬇️", "➡️" }   // Кнопки вниз и вправо
    })
        {
            ResizeKeyboard = true // Делаем кнопки масштабируемыми
        };

        // Отправляем текущее состояние поля и кнопки управления
        await botClient.SendTextMessageAsync(chatId, "Игра 2048. Управляй плитками с помощью кнопок.", replyMarkup: keyboard);
        await Send2048Board(chatId, board);  // Отправляем начальное состояние поля
    }

    private static async Task Handle2048Input(Message message)
    {
        var userId = message.From.Id;
        var board = userGames[userId].Board;  // Получаем текущее состояние игрового поля

        bool moved = false;

        // Логика обработки направления движения
        switch (message.Text)
        {
            case "⬆️":
                moved = MoveUp(board);  // Перемещение вверх
                break;
            case "⬇️":
                moved = MoveDown(board);  // Перемещение вниз
                break;
            case "⬅️":
                moved = MoveLeft(board);  // Перемещение влево
                break;
            case "➡️":
                moved = MoveRight(board);  // Перемещение вправо
                break;
            default:
                await botClient.SendTextMessageAsync(message.Chat.Id, "Неверная команда.");
                return;
        }

        // Если произошел успешный сдвиг, добавляем новую плитку и обновляем состояние поля
        if (moved)
        {
            AddRandomTile(board);  // Добавляем новую плитку
            await Send2048Board(message.Chat.Id, board);  // Отправляем обновленное состояние поля пользователю
        }
        else
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Невозможно сдвинуть в этом направлении.");
        }
    }


    // Реализация перемещений (MoveUp, MoveDown, MoveLeft, MoveRight)
    private static bool MoveUp(int[,] board)
    {
        bool moved = false;
        for (int col = 0; col < BoardSize; col++)
        {
            List<int> newColumn = new List<int>();
            for (int row = 0; row < BoardSize; row++)
            {
                if (board[row, col] != 0)
                {
                    if (newColumn.Count > 0 && newColumn.Last() == board[row, col])
                    {
                        newColumn[newColumn.Count - 1] *= 2;  // Объединяем плитки
                        moved = true;
                    }
                    else
                    {
                        newColumn.Add(board[row, col]);
                    }
                }
            }
            // Заполняем оставшуюся часть столбца нулями
            for (int i = 0; i < BoardSize; i++)
            {
                if (i < newColumn.Count)
                {
                    if (board[i, col] != newColumn[i])
                    {
                        moved = true;  // Обновляем moved, если произошло перемещение
                    }
                    board[i, col] = newColumn[i];
                }
                else
                {
                    board[i, col] = 0;
                }
            }
        }
        return moved;
    }

    private static bool MoveDown(int[,] board)
    {
        bool moved = false;
        for (int col = 0; col < BoardSize; col++)
        {
            List<int> newColumn = new List<int>();
            for (int row = BoardSize - 1; row >= 0; row--)
            {
                if (board[row, col] != 0)
                {
                    if (newColumn.Count > 0 && newColumn.Last() == board[row, col])
                    {
                        newColumn[newColumn.Count - 1] *= 2;  // Объединяем плитки
                        moved = true;
                    }
                    else
                    {
                        newColumn.Add(board[row, col]);
                    }
                }
            }
           
            for (int i = 0; i < BoardSize; i++)
            {
                if (i < newColumn.Count)
                {
                    if (board[BoardSize - 1 - i, col] != newColumn[i])
                    {
                        moved = true;  // Обновляем moved, если произошло перемещение
                    }
                    board[BoardSize - 1 - i, col] = newColumn[i];
                }
                else
                {
                    board[BoardSize - 1 - i, col] = 0;
                }
            }
        }
        return moved;
    }

    private static bool MoveLeft(int[,] board)
    {
        bool moved = false;
        for (int row = 0; row < BoardSize; row++)
        {
            List<int> newRow = new List<int>();
            for (int col = 0; col < BoardSize; col++)
            {
                if (board[row, col] != 0)
                {
                    if (newRow.Count > 0 && newRow.Last() == board[row, col])
                    {
                        newRow[newRow.Count - 1] *= 2;  // Объединяем плитки
                        moved = true;
                    }
                    else
                    {
                        newRow.Add(board[row, col]);
                    }
                }
            }
           
            for (int i = 0; i < BoardSize; i++)
            {
                if (i < newRow.Count)
                {
                    if (board[row, i] != newRow[i])
                    {
                        moved = true;  // Обновляем moved, если произошло перемещение
                    }
                    board[row, i] = newRow[i];
                }
                else
                {
                    board[row, i] = 0;
                }
            }
        }
        return moved;
    }

    private static bool MoveRight(int[,] board)
    {
        bool moved = false;
        for (int row = 0; row < BoardSize; row++)
        {
            List<int> newRow = new List<int>();
            for (int col = BoardSize - 1; col >= 0; col--)
            {
                if (board[row, col] != 0)
                {
                    if (newRow.Count > 0 && newRow.Last() == board[row, col])
                    {
                        newRow[newRow.Count - 1] *= 2;  // Объединяем плитки
                        moved = true;
                    }
                    else
                    {
                        newRow.Add(board[row, col]);
                    }
                }
            }
            // Заполняем оставшуюся часть строки нулями
            for (int i = 0; i < BoardSize; i++)
            {
                if (i < newRow.Count)
                {
                    if (board[row, BoardSize - 1 - i] != newRow[i])
                    {
                        moved = true;  // Обновляем moved, если произошло перемещение
                    }
                    board[row, BoardSize - 1 - i] = newRow[i];
                }
                else
                {
                    board[row, BoardSize - 1 - i] = 0;
                }
            }
        }
        return moved;
    }

    private static void AddRandomTile(int[,] board)
    {
        Random rand = new Random();
        List<(int, int)> emptyCells = new List<(int, int)>();

        for (int i = 0; i < BoardSize; i++)
        {
            for (int j = 0; j < BoardSize; j++)
            {
                if (board[i, j] == 0)
                {
                    emptyCells.Add((i, j));
                }
            }
        }

        if (emptyCells.Count > 0)
        {
            var (x, y) = emptyCells[rand.Next(emptyCells.Count)];
            board[x, y] = rand.Next(0, 10) == 0 ? 4 : 2;  // 90% шанс на 2, 10% на 4
        }
    }

    // Генерация изображения игрового поля для "2048" (без цвета)
    private static async Task Send2048Board(long chatId, int[,] board)
    {
        using (Bitmap bitmap = new Bitmap(400, 400))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.White);
                for (int i = 0; i < BoardSize; i++)
                {
                    for (int j = 0; j < BoardSize; j++)
                    {
                        int value = board[i, j];
                        g.FillRectangle(Brushes.LightGray, j * 100, i * 100, 100, 100);
                        g.DrawRectangle(Pens.Black, j * 100, i * 100, 100, 100);
                        if (value > 0)
                        {
                            g.DrawString(value.ToString(), new Font("Arial", 24), Brushes.Black, j * 100 + 25, i * 100 + 35);
                        }
                    }
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);

                    // Создание InputFile для передачи в SendPhotoAsync
                    var inputFile = new InputFileStream(ms, "board.png");

                    // Отправка фотографии с игровым полем
                    await botClient.SendPhotoAsync(chatId, inputFile);
                }
            }
        }
    }


    // Обработка ошибок
    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, System.Threading.CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Ошибка Telegram API: {apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }

    // Состояние игры для каждого пользователя
    private class GameState
    {
        public string CurrentGame { get; set; }
        public int SecretNumber { get; set; }
        public int[,] Board { get; set; }
    }
}
