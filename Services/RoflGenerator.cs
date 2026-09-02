using System.IO;
using System.Text.RegularExpressions;
using Barakhloskop.Models;

namespace Barakhloskop.Services;

/// <summary>
/// Генератор рофло-текста: вердикты по файлу, фразы во время сканирования,
/// звания «барахольщика» и подписи к статистике.
/// </summary>
public sealed class RoflGenerator
{
    private readonly Random _random = Random.Shared;

    private static readonly string[] ScanPhrases =
    {
        "Опрашиваю жёсткий диск под протокол…",
        "Ищу то, что вы скачали и забыли…",
        "Раскапываю папку «Новая папка (3)»…",
        "Считаю мемы 2014 года…",
        "Проверяю, всё ли ещё стыдно за эту музыку…",
        "Сканирую цифровые залежи…",
        "Вспоминаю за вас, что тут вообще лежит…",
        "Инвентаризация барахла в процессе…",
        "Прочёсываю диск мелкой гребёнкой…",
        "Обхожу папки, куда вы не заходили с прошлой пятилетки…",
        "Собираю доказательства вашего плюшкинизма…",
        "Ищу скриншот, который вы искали два года…",
        "Загружаю ностальгию, 3%…",
        "Досматриваю фотографии еды…",
        "Разбираю «Загрузки» на атомы…",
        "Индексирую бесценные шедевры и один вебинар…",
        "Проверяю папки на наличие смысла…",
        "Обнюхиваю метаданные…",
        "Прикидываю, сколько тут можно удалить (спойлер: много)…",
        "Пересчитываю дубликаты котиков…"
    };

    private static readonly string[] AncientVerdicts =
    {
        "Этот файл старше некоторых ваших знакомств.",
        "Археологическая находка. Аккуратнее, может рассыпаться.",
        "Файл лежит так давно, что оброс мхом.",
        "Датировка внушает уважение. Почти артефакт.",
        "Этот файл помнит вас другим человеком.",
        "Извлечено из культурного слоя диска."
    };

    private static readonly string[] FreshVerdicts =
    {
        "Свежак. Даже пыль осесть не успела.",
        "Файл настолько новый, что ещё пахнет загрузкой.",
        "Только что скачано и уже забыто. Классика.",
        "Горячее, только из интернета."
    };

    private static readonly string[] HugeVerdicts =
    {
        "Весит как небольшая планета. Диск передаёт привет.",
        "Этот файл единолично держит ваш диск в заложниках.",
        "Тяжеловес. Кандидат на выселение.",
        "Один такой файл — и «Загрузки» уже не те."
    };

    private static readonly string[] TinyVerdicts =
    {
        "Крошечный. Мог бы и не рождаться.",
        "Микроскопический файл с огромным самомнением.",
        "Весит меньше, чем ваше желание разобрать папки.",
        "Настолько маленький, что почти концепция."
    };

    private static readonly string[] ImageVerdicts =
    {
        "Скорее всего, скриншот чего-то важного. Уже не важного.",
        "Сохранено «на потом». Потом не наступило.",
        "Судя по имени, это мем. Судя по дате — мем-пенсионер.",
        "Картинка, ради которой вы точно чистили место.",
        "Фотография, которую вы обещали обработать."
    };

    private static readonly string[] AudioVerdicts =
    {
        "Музыка, под которую вы делали вид, что работаете.",
        "Трек из плейлиста, который вы никому не покажете.",
        "Аудиофайл. Возможно, это был рингтон.",
        "Судя по битрейту, качали через торрент в три ночи.",
        "Наушники надеть или пусть колонки решают?"
    };

    private static readonly string[] VideoVerdicts =
    {
        "Видео, которое вы посмотрели один раз и оставили навсегда.",
        "Скорее всего, туториал, который вы не досмотрели.",
        "Запись на 40 минут, из которых полезны 12 секунд.",
        "Видео из времён, когда 480p считалось приличным.",
        "Есть шанс, что это ваш собственный геймплей. Мужайтесь."
    };

    private static readonly string[] GenericVerdicts =
    {
        "Файл как файл. Ничего личного.",
        "Ни рыба ни мясо, но полежит.",
        "Комиссия постановила: пусть живёт.",
        "Экспертиза не выявила ничего компрометирующего.",
        "Проходной вариант. Двигаемся дальше."
    };

    private static readonly (Regex Pattern, string Verdict)[] NameJokes =
    {
        (new Regex(@"^(img|image|photo)[_\-\s]?\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Имя файла — просто номер. Дизайн-мышление в лучшем виде."),
        (new Regex(@"^(screenshot|снимок|скрин)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Скриншот. Контекст утерян навсегда."),
        (new Regex(@"(final|финал|итог)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "В названии «final». Значит, где-то рядом лежит final_2."),
        (new Regex(@"(new|новый|новая|копия|copy)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "«Копия». Оригинал, разумеется, не найден."),
        (new Regex(@"^(video|vid|movie|clip)[_\-\s]?\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Название максимально нейтральное. Подозрительно нейтральное."),
        (new Regex(@"(temp|tmp|test|тест|проба)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Временный файл, живущий постояннее вас."),
        (new Regex(@"(\d{4})[-_\.](\d{2})[-_\.](\d{2})", RegexOptions.Compiled),
            "Дата в имени файла: аккуратность на грани фанатизма."),
        (new Regex(@"[а-яё]{3,}\s+[а-яё]{3,}", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Имя на русском с пробелами. Смело."),
        (new Regex(@"(без\s?названия|untitled|unnamed)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "«Без названия». Творческий подход к архивации."),
        (new Regex(@"(\(1\)|\(2\)|\(3\))", RegexOptions.Compiled),
            "Скачано несколько раз. Первые попытки, видимо, не считались.")
    };

    private static readonly (Regex Pattern, string Verdict)[] FolderJokes =
    {
        (new Regex(@"(downloads|загрузки)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Найдено в «Загрузках» — там, где файлы уходят на пенсию."),
        (new Regex(@"(desktop|рабочий стол)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Лежит на рабочем столе. Рабочий стол не в восторге."),
        (new Regex(@"(новая папка|new folder)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Папка называется «Новая папка». Систематизация уровня «бог»."),
        (new Regex(@"(музыка|music)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Из музыкальной коллекции. Вкус не оценивается, только объём."),
        (new Regex(@"(мемы|memes|приколы|рофл)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Обнаружен склад мемов. Уважаю подход."),
        (new Regex(@"(документы|documents)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            "Медиафайл в «Документах». Порядок — понятие гибкое.")
    };

    private static readonly (int Threshold, string Title, string Comment)[] HoarderRanks =
    {
        (0, "Аскет", "Диск почти пустой. Вы точно пользуетесь компьютером?"),
        (50, "Начинающий собиратель", "Скромно. Есть куда расти."),
        (300, "Уверенный барахольщик", "Стабильный поток скачиваний, ничего лишнего… почти."),
        (1_500, "Коллекционер", "Уже коллекция. Осталось придумать, зачем."),
        (5_000, "Цифровой Плюшкин", "Вы не удаляете ничего. Никогда."),
        (15_000, "Хранитель архива", "Ваш диск можно сдавать в музей как экспонат."),
        (50_000, "Легенда терабайтов", "Здесь нужен не разбор файлов, а археологическая экспедиция.")
    };

    private static readonly string[] EmptyResultLines =
    {
        "Ничего не найдено. Либо вы святой, либо папки не те.",
        "Пусто. Впервые вижу такой чистый диск. Проверьте фильтры.",
        "Ноль файлов. Может, стоит поискать пошире?"
    };

    public string NextScanPhrase() => ScanPhrases[_random.Next(ScanPhrases.Length)];

    public string EmptyResultLine() => EmptyResultLines[_random.Next(EmptyResultLines.Length)];

    /// <summary>Два-три предложения вердикта по конкретному файлу.</summary>
    public string Verdict(MediaFile file)
    {
        var lines = new List<string>(3);
        var ageDays = (DateTime.Now - file.Modified).TotalDays;

        if (ageDays > 365 * 5) lines.Add(Pick(AncientVerdicts));
        else if (ageDays < 3) lines.Add(Pick(FreshVerdicts));

        if (file.Size > 700L * 1024 * 1024) lines.Add(Pick(HugeVerdicts));
        else if (file.Size < 40L * 1024) lines.Add(Pick(TinyVerdicts));

        var baseName = Path.GetFileNameWithoutExtension(file.Name);
        foreach (var (pattern, verdict) in NameJokes)
        {
            if (pattern.IsMatch(baseName))
            {
                lines.Add(verdict);
                break;
            }
        }

        foreach (var (pattern, verdict) in FolderJokes)
        {
            if (pattern.IsMatch(file.FolderName))
            {
                lines.Add(verdict);
                break;
            }
        }

        if (baseName.Length > 45) lines.Add("Название длиннее, чем ваш список дел. И такое же нечитаемое.");
        if (file.Depth > 9) lines.Add($"Глубина вложения — {file.Depth}. Без карты и фонаря туда лучше не ходить.");

        if (lines.Count < 2)
        {
            lines.Add(file.Kind switch
            {
                MediaKind.Image => Pick(ImageVerdicts),
                MediaKind.Audio => Pick(AudioVerdicts),
                MediaKind.Video => Pick(VideoVerdicts),
                _ => Pick(GenericVerdicts)
            });
        }

        if (lines.Count < 2) lines.Add(Pick(GenericVerdicts));

        // Не больше трёх строк, чтобы карточка не расползалась.
        return string.Join(" ", lines.Distinct().Take(3));
    }

    /// <summary>Оценка «уровня барахольщика» по количеству найденного.</summary>
    public (string Title, string Comment) HoarderRank(int matches)
    {
        var result = HoarderRanks[0];
        foreach (var rank in HoarderRanks)
            if (matches >= rank.Threshold)
                result = rank;
        return (result.Title, result.Comment);
    }

    /// <summary>Итоговая подпись после завершения сканирования.</summary>
    public string ScanSummary(ScanResult result)
    {
        if (result.Canceled) return "Сканирование прервано. Диск облегчённо выдохнул.";
        if (result.Matches == 0) return EmptyResultLine();
        if (result.Truncated) return "Лимит результатов исчерпан. Файлов больше, чем нервов у программы.";

        var perSecond = result.Elapsed.TotalSeconds > 0.1
            ? result.FilesSeen / result.Elapsed.TotalSeconds
            : result.FilesSeen;

        return perSecond switch
        {
            > 20_000 => "Диск шустрый: обход прошёл на реактивной тяге.",
            > 5_000 => "Скорость приличная, диск не жаловался.",
            > 800 => "Обход завершён в штатном режиме.",
            _ => "Диск сопротивлялся, но мы победили."
        };
    }

    private string Pick(string[] source) => source[_random.Next(source.Length)];
}
