# Описание маршрутов

Приложение построено на ASP.NET Core MVC: сервер возвращает HTML, формы отправляются
обычными POST-запросами. Все POST-запросы защищены anti-forgery токеном, все разделы
кроме регистрации/входа требуют cookie-аутентификации.

## Аккаунты

| Метод | Маршрут | Назначение |
| --- | --- | --- |
| GET | `/Account/Register` | Форма регистрации |
| POST | `/Account/Register` | Создание пользователя и вход |
| GET | `/Account/Login` | Форма входа |
| POST | `/Account/Login` | Проверка пароля, выдача cookie |
| POST | `/Account/Logout` | Выход |
| GET | `/Account/AccessDenied` | Страница «доступ запрещён» |

Валидация: имя 2–100 символов, корректный email, пароль от 6 символов,
подтверждение пароля. Email приводится к нижнему регистру и уникален.

## Файлы и папки

| Метод | Маршрут | Параметры | Назначение |
| --- | --- | --- | --- |
| GET | `/Files` | `folderId`, `search`, `sort` | Список папок и файлов |
| POST | `/Files/Upload` | `files[]`, `folderId` | Загрузка файлов |
| GET | `/Files/Download/{id}` | — | Скачивание файла |
| POST | `/Files/CreateFolder` | `name`, `parentId` | Создание папки |
| POST | `/Files/RenameFolder` | `id`, `name` | Переименование папки |
| POST | `/Files/MoveFolder` | `id`, `targetFolderId` | Перемещение папки |
| POST | `/Files/DeleteFolder` | `id` | Папка в корзину (с содержимым) |
| POST | `/Files/RenameFile` | `id`, `name` | Переименование файла |
| POST | `/Files/MoveFile` | `id`, `targetFolderId` | Перемещение файла |
| POST | `/Files/DeleteFile` | `id` | Файл в корзину |

Параметр `sort` принимает значения: `name`, `name_desc`, `size`, `size_desc`,
`date`, `date_desc`. Поиск выполняется по имени внутри текущей папки.

## Корзина

| Метод | Маршрут | Параметры | Назначение |
| --- | --- | --- | --- |
| GET | `/Files/Trash` | — | Список удалённого |
| POST | `/Files/RestoreFile` | `id` | Восстановить файл |
| POST | `/Files/RestoreFolder` | `id` | Восстановить папку с содержимым |
| POST | `/Files/PurgeFile` | `id` | Удалить файл навсегда |
| POST | `/Files/PurgeFolder` | `id` | Удалить папку с содержимым навсегда |
| POST | `/Files/EmptyTrash` | — | Очистить корзину |

## Коды ответов и ошибки

| Код | Когда возвращается |
| --- | --- |
| `200` | Страница успешно отрисована |
| `302` | После успешного POST — редирект с сообщением в `TempData` |
| `404` | Объект не найден **или принадлежит другому пользователю** |
| `400` | Ошибка валидации формы / отсутствует anti-forgery токен |
| `413` | Превышен максимальный размер запроса |

Ошибки бизнес-логики (нет места, дубликат имени, нельзя переместить папку внутрь себя)
показываются пользователю сообщением и не меняют данные.

## Пример: загрузка через curl

```bash
# 1. Получить cookie и HTML страницы входа
curl -c cookies.txt -o login.html http://localhost:5080/Account/Login

# 2. Взять значение __RequestVerificationToken из login.html и войти
curl -b cookies.txt -c cookies.txt -X POST http://localhost:5080/Account/Login \
  --data-urlencode "Email=admin@storage.local" \
  --data-urlencode "Password=Admin123!" \
  --data-urlencode "__RequestVerificationToken=<token>"

# 3. Взять новый токен со страницы /Files и загрузить файл
curl -b cookies.txt -c cookies.txt -X POST http://localhost:5080/Files/Upload \
  -F "files=@report.pdf" -F "folderId=" \
  -F "__RequestVerificationToken=<token>"
```

Для ручной проверки проще открыть приложение в браузере: формы уже содержат токен.
