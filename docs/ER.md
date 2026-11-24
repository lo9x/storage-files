# ER-диаграмма базы данных

СУБД: **SQLite** (EF Core 10, Code First, миграции).

```mermaid
erDiagram
    Users ||--o{ Folders : "владеет"
    Users ||--o{ Files : "владеет"
    Folders ||--o{ Folders : "родитель / потомки"
    Folders ||--o{ Files : "содержит"

    Users {
        int Id PK
        string Email UK "уникальный, lower-case"
        string DisplayName
        string PasswordHash "PBKDF2 через PasswordHasher"
        string Role "User | Admin"
        long StorageLimitBytes "лимит хранения"
        datetime CreatedAt
    }

    Folders {
        int Id PK
        int OwnerId FK "Users.Id"
        int ParentId FK "Folders.Id, null = корень"
        string Name
        bool IsDeleted "мягкое удаление"
        datetime DeletedAt
        datetime CreatedAt
        datetime UpdatedAt
    }

    Files {
        int Id PK
        int OwnerId FK "Users.Id"
        int FolderId FK "Folders.Id, null = корень"
        string Name "имя для пользователя"
        string StoredName "guid + расширение на диске"
        string ContentType
        long SizeBytes
        bool IsDeleted
        datetime DeletedAt
        datetime CreatedAt
        datetime UpdatedAt
    }
```

## Пояснения

- **Users → Folders / Files** — каскадное удаление на уровне БД не используется
  в пользовательских сценариях (удаление мягкое), но настроено для очистки данных.
- **Folders.ParentId** — самоссылка, `Restrict` защищает от случайного удаления
  папки с вложенными папками. Вложенность обрабатывается в `FilesController`.
- **Files.FolderId = null** — файл лежит в корне личного пространства.
- Индексы:
  - `Users.Email` — уникальный;
  - `Folders(OwnerId, ParentId)` и `Folders(OwnerId, IsDeleted)`;
  - `Files(OwnerId, FolderId)` и `Files(OwnerId, IsDeleted)`.
- Все выборки в приложении дополнительно фильтруются по `OwnerId`, поэтому
  пользователь не может обратиться к чужим данным даже зная идентификатор.

## Схема хранения на диске

```text
App_Data/
├── storagefiles.db          # база SQLite
└── storage/
    ├── 1/                   # userId = 1
    │   ├── 4f2c...e1.pdf
    │   └── 9ab1...77.png
    └── 2/
        └── 1c8d...44.zip
```

Отображаемые имена хранятся только в БД, поэтому переименование файла не трогает диск.
