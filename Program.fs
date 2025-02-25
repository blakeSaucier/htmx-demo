﻿open FsToolkit.ErrorHandling
open Giraffe
open Saturn
open System

[<AutoOpen>]
module Domain =
    /// The input search request parameters.
    [<CLIMutable>]
    type RawSearchRequest = {
        SearchInput: string
        SortColumn: string
        SortDirection: string
    }

    /// Represents a report for a specific country.
    type CountryReport = {
        Country: string
        Code: string option
        Nuclear: float
        EnergyImports: float
        RenewableEnergyConsumption: float
        FossilFuelEnergyConsumption: float
    }

    type NumericColumn =
        | Renewables
        | Imports
        | Fossil
        | Nuclear

    type TextColumn = Country

    /// The sort column to use.
    type SortColumn =
        | TextColumn of TextColumn
        | NumericColumn of NumericColumn

        member this.AsString =
            match this with
            | TextColumn Country -> "Country"
            | NumericColumn Renewables -> "Renewables"
            | NumericColumn Imports -> "Imports"
            | NumericColumn Fossil -> "Fossil"
            | NumericColumn Nuclear -> "Nuclear"

        static member TryOfString v =
            match v with
            | "Country" -> Some(TextColumn Country)
            | "Renewables" -> Some(NumericColumn Renewables)
            | "Imports" -> Some(NumericColumn Imports)
            | "Fossil" -> Some(NumericColumn Fossil)
            | "Nuclear" -> Some(NumericColumn Nuclear)
            | _ -> None

    type SortDirection =
        | Ascending
        | Descending

        member this.AsString =
            match this with
            | Ascending -> "Ascending"
            | Descending -> "Descending"

        static member TryOfString v =
            match v with
            | "Ascending" -> Some Ascending
            | "Descending" -> Some Descending
            | _ -> None

    type Sort = SortColumn * SortDirection

    let defaultSort = TextColumn Country, SortDirection.Ascending

    type SearchRequest =
        {
            SearchInput: string option
            Sort: Sort
        }

        static member OfRawSearchRequest(request: RawSearchRequest) = {
            SearchInput = request.SearchInput |> Option.ofObj
            Sort =
                let userSort = option {
                    let! col = SortColumn.TryOfString request.SortColumn
                    let! dir = SortDirection.TryOfString request.SortDirection
                    return col, dir
                }

                userSort |> Option.defaultValue defaultSort
        }

    /// Sorts the supplied reports using the specified sort column.
    let sortBy (column, direction) =
        match column with
        | TextColumn Country ->
            match direction with
            | Ascending -> Seq.sortBy (fun c -> c.Country)
            | Descending -> Seq.sortByDescending (fun c -> c.Country)
        | NumericColumn column ->
            let column =
                match column with
                | Imports -> fun c -> c.EnergyImports
                | Fossil -> fun c -> c.FossilFuelEnergyConsumption
                | Nuclear -> fun c -> c.Nuclear
                | Renewables -> fun c -> c.RenewableEnergyConsumption

            match direction with
            | Ascending -> Seq.sortBy column
            | Descending -> Seq.sortByDescending column

module View =
    open Giraffe.Htmx
    open Giraffe.ViewEngine
    open Giraffe.ViewEngine.Htmx

    /// The initial start page of the application.
    let startingPage =
        html [] [
            head [] [
                link [
                    _rel "stylesheet"
                    _href "https://cdn.jsdelivr.net/npm/@tabler/core@latest/dist/css/tabler.min.css"
                ]
                link [
                    _rel "stylesheet"
                    _href "https://cdn.jsdelivr.net/npm/@tabler/core@latest/dist/css/tabler-flags.min.css"
                ]
                script [
                    _src "https://cdn.jsdelivr.net/npm/@tabler/core@latest/dist/js/tabler.min.js"
                ] []
            ]
            Script.minified
            body [ _class "theme-light" ] [
                div [ _class "page" ] [
                    div [ _class "page-wrapper" ] [
                        div [ _class "page-body" ] [
                            div [ _class "container-xl" ] [
                                div [ _class "row row-cards" ] [
                                    div [ _class "col-12" ] [
                                        form [ _class "card" ] [
                                            div [ _class "card-header card-header-light" ] [
                                                h4 [ _class "card-title" ] [
                                                    str "World Bank Energy Statistics"
                                                    span [ _class "card-subtitle" ] [
                                                        str "F#, HTMX and Tabler demonstrator"
                                                    ]
                                                ]
                                            ]
                                            div [ _class "card-body" ] [
                                                div [ _class "row" ] [
                                                    div [] [
                                                        label [ _for "search-input"; _class "form-label" ] [
                                                            str "Search Term"
                                                        ]
                                                        datalist [ _id "search-suggestions" ] []
                                                        div [ _class "input-icon mb-3" ] [
                                                            input [
                                                                _id "search-input"
                                                                _class "form-control"
                                                                _list "search-suggestions"
                                                                _name "searchinput"
                                                                _placeholder
                                                                    "Enter an exact or partial country, or a region."
                                                                _type "search"

                                                                _hxTrigger "keyup changed delay:500ms"
                                                                _hxPost "/search-suggestions"
                                                                _hxTarget "#search-suggestions"
                                                                _hxSwap HxSwap.OuterHtml
                                                            ]
                                                            span [
                                                                _id "spinner"
                                                                _class "htmx-indicator input-icon-addon"
                                                            ] [
                                                                div [
                                                                    _class "spinner-border spinner-border-sm"
                                                                    attr "role" "status"
                                                                ] []
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                            div [ _class "card-footer d-flex" ] [
                                                button [
                                                    _class "btn btn-primary"
                                                    _id "search-button"
                                                    _name "searchButton"
                                                    _type "submit"

                                                    _hxPost "/do-search"
                                                    _hxInclude "#search-input"
                                                    _hxTarget "#search-results"
                                                    _hxIndicator "#spinner"
                                                ] [ str " Search!" ]
                                            ]
                                        ]
                                        div [ _id "search-results"; _class "mt-3" ] []
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
        |> htmlView

    /// Builds a table based on all reports.
    let createReportsTable sort reports =
        let pickCellColour (success, warning) value =
            [ "success", success; "warning", warning ]
            |> List.tryFind (fun (_, f) -> f value)
            |> Option.map fst
            |> Option.defaultValue "danger"
            |> sprintf "bg-%s"

        let buildProgressBarCell value pickers =
            let colour = value |> pickCellColour pickers

            td [] [
                div [ _class "row align-items-center" ] [
                    div [ _class "col-12 col-lg-auto" ] [
                        div [ _class "progress"; _style "width: 5rem" ] [
                            div [
                                _class $"progress-bar {colour}"
                                _style $"width: {value}%%"
                                attr "role" "progressbar"
                            ] []
                        ]
                    ]
                    div [ _class "col" ] [ str $"%.2f{value}%%" ]
                ]
            ]

        let higherIsBetter = (fun x -> x > 40.), (fun x -> x > 10.)
        let lowerIsBetter = (fun x -> x < 25.), (fun x -> x < 50.)

        div [ _class "card" ] [
            div [ _id "table-default"; _class "table-responsive" ] [
                table [ _class "table card-table table-striped datatable" ] [
                    thead [] [
                        tr [] [
                            let makeTh value (column: SortColumn) =
                                th [] [
                                    button [
                                        let nextDirection, sortHeader =
                                            match sort with
                                            | currentColumn, Ascending when column = currentColumn -> Descending, "asc"
                                            | currentColumn, Descending when column = currentColumn -> Ascending, "desc"
                                            | _ -> Ascending, ""

                                        _class $"table-sort {sortHeader}"

                                        _hxInclude "#search-input"
                                        _hxTarget "#search-results"
                                        _hxPost "/do-search"
                                        _hxTrigger "click"

                                        _hxVals
                                            $"{{ \"sortColumn\" : \"{column.AsString}\", \"sortDirection\" : \"{nextDirection.AsString}\" }}"
                                    ] [ str value ]
                                ]

                            makeTh "Country" (TextColumn Country)
                            makeTh "Energy Imports (% of total)" (NumericColumn Imports)
                            makeTh "Renewables (% of total)" (NumericColumn Renewables)
                            makeTh "Fossil Fuels (% of total)" (NumericColumn Fossil)
                            makeTh "Nuclear & Other (% of total)" (NumericColumn Nuclear)
                        ]
                    ]
                    tbody [ _class "table-group-divider" ] [
                        for report in reports do
                            tr [] [
                                td [] [
                                    match report.Code with
                                    | Some code -> span [ _class $"flag flag-country-{code}" ] []
                                    | None -> ()
                                    str $" {report.Country} "
                                ]
                                buildProgressBarCell report.EnergyImports lowerIsBetter
                                buildProgressBarCell report.RenewableEnergyConsumption higherIsBetter
                                buildProgressBarCell report.FossilFuelEnergyConsumption lowerIsBetter
                                buildProgressBarCell report.Nuclear higherIsBetter
                            ]
                    ]
                ]
            ]
        ]

    /// Creates a datalist for the supplied countries.
    let createCountriesSuggestions destinations =
        datalist [ _id "search-suggestions" ] [
            for destination: string in destinations do
                option [ _value destination ] []
        ]

module DataAccess =
    open FSharp.Data
    open Microsoft.Extensions.Caching.Memory
    open Polly
    open Polly.Caching.Memory

    let cachePolicy =
        let memoryCacheProvider =
            let memoryCache = new MemoryCache(MemoryCacheOptions())
            MemoryCacheProvider memoryCache

        Policy.Cache(memoryCacheProvider, TimeSpan.FromMinutes 5)


    type private CountryCodesWiki = HtmlProvider<"https://en.m.wikipedia.org/wiki/List_of_ISO_3166_country_codes">

    let private tryGetCountryIsoCode =
        let lookup =
            readOnlyDict [
                for row in CountryCodesWiki.GetSample().Tables.``Current ISO 3166 country codesEdit``.Rows do
                    row.``ISO 3166-1[2] - Alpha-3 code[5]``, row.``ISO 3166-1[2] - Alpha-2 code[5]``
            ]

        fun value -> lookup |> Option.tryGetValue value |> Option.map (fun r -> r.ToLower())

    let private ctx = WorldBankData.GetDataContext()
    let private allCountries = ctx.Countries |> Seq.toList
    let private allRegions = ctx.Regions |> Seq.toList

    let private countriesAndRegions =
        let countries = allCountries |> List.map (fun country -> country.Name.Trim())
        let regions = allRegions |> List.map (fun region -> region.Name.Trim())
        countries @ regions

    let private containsText (text: string) (v: string) =
        v.Trim().Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase)

    let private matches (text: string) (v: string) =
        v.Trim().Equals(text.Trim(), StringComparison.CurrentCultureIgnoreCase)

    let tryCreateReport country =
        let actualQuery (country: WorldBankData.ServiceTypes.Country) = option {
            let! nuclear =
                country.Indicators.``Alternative and nuclear energy (% of total energy use)``.Values
                |> Seq.tryLast

            let! imports =
                country.Indicators.``Energy imports, net (% of energy use)``.Values
                |> Seq.tryLast

            let! renewables =
                country.Indicators.``Renewable energy consumption (% of total final energy consumption)``.Values
                |> Seq.tryLast

            let! fossils =
                country.Indicators.``Fossil fuel energy consumption (% of total)``.Values
                |> Seq.tryLast

            return {
                Country = country.Name
                Code = tryGetCountryIsoCode country.Code
                Nuclear = nuclear
                EnergyImports = imports
                RenewableEnergyConsumption = renewables
                FossilFuelEnergyConsumption = fossils
            }
        }

        cachePolicy.Execute((fun ctx -> actualQuery country), Context $"{country.Code}")

    /// Gets the top ten destinations that contain the supplied text.
    let findDestinations (text: string option) =
        match text with
        | None -> countriesAndRegions
        | Some text -> countriesAndRegions |> List.filter (containsText text)
        |> List.truncate 10

    /// Finds all country-level reports that contain the supplied text.
    let findReportsByCountries sort (text: string) =
        allCountries
        |> Seq.filter (fun country -> country.Name |> containsText text)
        |> Seq.choose tryCreateReport
        |> sortBy sort
        |> Seq.toList

    /// Looks for an exact match of a country or region based on the text supplied. Tries a country first; if no match,
    /// check for a region - if that matches, all countries within that region are returned.
    let tryExactMatchReport sort (text: string) =
        let matchingCountry = allCountries |> List.tryFind (fun c -> c.Name |> matches text)

        match matchingCountry with
        | Some country -> tryCreateReport country |> Option.map List.singleton
        | None ->
            allRegions
            |> List.tryFind (fun region -> region.Name |> matches text)
            |> Option.map (fun region -> region.Countries |> Seq.choose tryCreateReport |> sortBy sort |> Seq.toList)

module Api =
    open Microsoft.AspNetCore.Http

    /// Finds destinations to suggest.
    let suggestDestinations next (ctx: HttpContext) = taskOption {
        let! request =
            ctx.BindModelAsync<RawSearchRequest>()
            |> Task.map SearchRequest.OfRawSearchRequest

        let destinations = DataAccess.findDestinations request.SearchInput
        return! htmlView (View.createCountriesSuggestions destinations) next ctx
    }

    /// Gets all energy reports using the query information supplied in the body.
    let findEnergyReports next (ctx: HttpContext) = task {
        let! request =
            ctx.BindModelAsync<RawSearchRequest>()
            |> Task.map SearchRequest.OfRawSearchRequest

        let reports =
            match request.SearchInput with
            | None -> DataAccess.findReportsByCountries request.Sort ""
            | Some searchInput ->
                DataAccess.tryExactMatchReport request.Sort searchInput
                |> Option.defaultWith (fun () -> DataAccess.findReportsByCountries request.Sort searchInput)

        return! htmlView (View.createReportsTable request.Sort reports) next ctx
    }

let allRoutes = router {
    get "/" View.startingPage
    post "/search-suggestions" Api.suggestDestinations
    post "/do-search" Api.findEnergyReports
}

let app = application { use_router allRoutes }

run app
