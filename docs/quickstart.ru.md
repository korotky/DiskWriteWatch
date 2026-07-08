# DiskWriteWatch: быстрый старт

DiskWriteWatch — служба Windows 10/11 x64, которая через kernel ETW собирает логические и физические записи на диски, агрегирует их в памяти и сохраняет в SQLite. Дополнительный драйвер не устанавливается.

## Установка

Распакуйте release ZIP и запустите PowerShell от администратора:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install.ps1 -DataDirectory 'E:\SystemMetrics\DiskWriteWatch'
```

Dashboard будет доступен только локально: <http://127.0.0.1:8765>. Служба `DiskWriteWatch` работает от `LocalSystem` и запускается с задержкой после старта Windows.

Основные пути:

- бинарник: `%ProgramFiles%\DiskWriteWatch\DiskWriteWatch.exe`;
- конфигурация: `%ProgramData%\DiskWriteWatch\config.json`;
- данные: путь `Storage.DataDirectory` из конфигурации.

## Интервалы

`Collection.BucketSeconds` — детализация агрегации и графиков, от 10 до 3600 секунд. `Storage.FlushIntervalSeconds` — период записи накопленных бакетов одной SQLite-транзакцией, тоже от 10 до 3600 секунд. Flush должен быть не меньше bucket и кратен ему.

Рекомендуемый стартовый режим — `60/300`: минутная детализация и запись раз в пять минут. Dashboard видит незаписанные бакеты прямо из RAM. При аварии возможна потеря не более одного flush-интервала; штатная остановка службы делает финальный flush.

Для сравнения нагрузки используйте режимы `60/60`, `60/300` и `300/300`, не меняя остальные параметры.

## Что означают данные

- Logical — объём записей в файлы, видимый File I/O ETW.
- Physical — запросы Disk I/O к физическому диску.
- WSL2 и Docker видны на стороне хоста как запись в `ext4.vhdx`/другой VHDX. Внутренний Linux-процесс из Windows ETW определить нельзя.
- Зелёная отметка на графике означает ещё не сохранённый RAM-бакет; после flush он заменяется сохранённой точкой без дублирования.
- Собственные записи DiskWriteWatch учитываются, но скрыты в топах по умолчанию.

Разница logical/physical нормальна из-за кэшей, компрессии, журналов файловой системы и отложенной записи.

## Обслуживание

После изменения `config.json`:

```powershell
.\restart.ps1
```

Удаление с сохранением конфигурации и данных:

```powershell
.\uninstall.ps1
```

Полное удаление данных из `%ProgramData%`:

```powershell
.\uninstall.ps1 -PurgeData
```

Если диск с базой недоступен, fallback на системный диск не выполняется: данные временно остаются в ограниченном RAM-буфере. Состояние видно в `/api/health` и на dashboard.

## Диагностика

Проверьте службу и API:

```powershell
Get-Service DiskWriteWatch
Invoke-RestMethod http://127.0.0.1:8765/api/health
```

Журнал службы находится в Windows Event Viewer. Для экспорта выбранного диапазона используйте кнопку CSV на dashboard или `/api/export.csv`.
