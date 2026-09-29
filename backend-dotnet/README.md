# Order API

A minimal ASP.NET Core Web API for browsing order data sourced from flat,
pipe-delimited export files (no schema documentation was provided — the
notes below capture what was reverse-engineered from the sample files).

## Running it

```
dotnet run
```

Then browse:
- `GET /api/orders` — list all orders (summary view)
- `GET /api/orders/{orderNumber}` — a single order with full line detail
  (e.g. `/api/orders/17890`)

A missing order returns `404 Not Found` with a small JSON body explaining
why, rather than a `200` with an empty/null body.

## How the data is shaped

The source files are **not normalized**. Every row in a file repeats the
order's header fields (`OrderNumber`, `OrderDate`, `CustomerNumber`,
`CustomerName`), while only the line-item fields (`OrderLineNumber`,
`ProductNumber`, `Quantity`, `Name`, `Description`, `Price`,
`ProductGroup`) actually vary row to row. That's a classic
header-repeated-per-line export, so the domain model un-flattens it into:

```
Order (1) ---- (many) OrderLine
```

`CsvOrderRepository` rebuilds this hierarchy by grouping rows on
`OrderNumber` as it reads them — it does **not** assume "one file = one
order", since that's an accident of how the sample data happens to be
split, not a guarantee.

## Notable data-quality decisions

- **Row format**: rows look like markdown table rows (`|a|b|c|`), not
  plain CSV. Splitting on `|` leaves an empty string at each end of the
  array, which is trimmed off before parsing.
- **Header row**: skipped by checking whether column 0 reads
  `"OrderNumber"`, not by line number — robust to a missing or
  duplicated header.
- **Empty `Description`**: valid, not an error (e.g. plain unbranded
  pieces have no description in the source data). Modeled as `string?`.
- **`ProductNumber`**: always a `string`. Formats are inconsistent across
  orders (`"123451324A"`, `"4000-AAA"`, `"A145"`) — it's an identifier,
  never used in arithmetic, so there's no reason to coerce it to a number.
- **`OrderNumber` / `CustomerNumber`**: also kept as `string`, for the
  same reason — they're identifiers, not quantities, and the file
  zero-pads `OrderLineNumber` (`"0001"`), which hints the source system
  treats these as formatted codes rather than pure integers.
- **Malformed rows**: a row with the wrong column count, or a value that
  fails to parse as the expected type, is logged and skipped rather than
  crashing the whole load. One bad row shouldn't take down the API.
- **File discovery**: the repository loads every `*.txt` file in `Data/`
  rather than hardcoding `Order1.txt`/`Order2.txt`/`Order3.txt` — adding
  another order is just dropping in another file.

## Structure

```
Controllers/   Thin HTTP layer — translates repository results to
               status codes (200/404). No parsing or business logic here.
Models/        Order, OrderLine — the domain, independent of how it's
               stored or how it's exposed over HTTP.
Models/Dtos/   Shapes actually returned by the API. Kept separate from
               the domain so the wire format can change without touching
               domain logic, and vice versa.
Services/      IOrderRepository + the CSV-backed implementation. The
               controller depends only on the interface — swapping the
               flat files for a database later means writing a new
               implementation of IOrderRepository and changing one line
               in Program.cs, nothing else.
Data/          The source .txt files, copied to the output directory on
               build.
```

## What's deliberately left out (given the time box)

- **Pagination/filtering** on `GET /api/orders` — straightforward to add
  (`?customerNumber=`, `?skip=&take=`) but not implemented to keep the
  surface area reviewable in the time available.
- **Automated tests** — the parsing logic in `CsvOrderRepository` is the
  highest-value thing to unit test (header skip, malformed row, empty
  description, multi-file grouping) if there's time left.
- **Caching invalidation / hot reload** of the data files — out of scope
  since there's no write path in this exercise.
