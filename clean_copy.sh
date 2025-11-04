#!/bin/bash

# Zatrzymuje skrypt w przypadku błędu
set -e

# Pobiera nazwę bieżącego katalogu (nazwa projektu)
CURRENT_DIR_NAME=${PWD##*/}

# Definiuje ścieżkę docelową dla czystej kopii
DEST_DIR="../${CURRENT_DIR_NAME}-Cleaned"

# Informuje użytkownika o rozpoczęciu procesu
echo "Tworzenie czystej kopii projektu w: ${DEST_DIR}"

# Kopiuje wszystkie pliki i foldery do nowego katalogu
# Używamy rsync dla lepszego feedbacku i potencjalnej elastyczności w przyszłości
rsync -a --info=progress2 --exclude='.git' . "${DEST_DIR}/"

# Przechodzi do nowego katalogu, aby upewnić się, że operacje usuwania są bezpieczne
cd "${DEST_DIR}"

# Znajduje i usuwa wszystkie foldery o nazwie 'bin'
echo "Usuwanie folderów 'bin'..."
find . -type d -name "bin" -exec rm -rf {} +

# Znajduje i usuwa wszystkie foldery o nazwie 'obj'
echo "Usuwanie folderów 'obj'..."
find . -type d -name "obj" -exec rm -rf {} +

# Powrót do oryginalnego katalogu
cd ..

# Informuje o zakończeniu
echo "Czyszczenie zakończone pomyślnie."
echo "Czysty projekt jest dostępny w: ${DEST_DIR}"
