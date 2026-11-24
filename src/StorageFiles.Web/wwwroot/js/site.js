(function () {
    'use strict';

    // Переименование: подставляем id и текущее имя в модальное окно.
    function bindRename(buttonSelector, formId) {
        var form = document.getElementById(formId);
        if (!form) {
            return;
        }

        var idInput = form.querySelector('[name="id"]');
        var nameInput = form.querySelector('[name="name"]');

        document.querySelectorAll(buttonSelector).forEach(function (button) {
            button.addEventListener('click', function () {
                idInput.value = button.dataset.id || '';
                nameInput.value = button.dataset.name || '';
                setTimeout(function () {
                    nameInput.focus();
                    nameInput.select();
                }, 200);
            });
        });
    }

    bindRename('[data-rename-folder]', 'renameFolderForm');
    bindRename('[data-rename-file]', 'renameFileForm');

    // Перемещение: один диалог для файлов и папок.
    var moveForm = document.getElementById('moveForm');
    if (moveForm) {
        document.querySelectorAll('[data-move]').forEach(function (button) {
            button.addEventListener('click', function () {
                var isFolder = button.dataset.kind === 'folder';
                moveForm.action = isFolder ? moveForm.dataset.folderUrl : moveForm.dataset.fileUrl;
                moveForm.querySelector('[name="id"]').value = button.dataset.id || '';
                moveForm.querySelector('[name="targetFolderId"]').value = '';

                var label = document.getElementById('moveItemName');
                if (label) {
                    label.textContent = button.dataset.name || '';
                }
            });
        });
    }

    // Очищаем поле при открытии окна создания папки.
    var createFolderModal = document.getElementById('createFolderModal');
    if (createFolderModal) {
        createFolderModal.addEventListener('shown.bs.modal', function () {
            var input = document.getElementById('newFolderName');
            if (input) {
                input.value = '';
                input.focus();
            }
        });
    }
})();
