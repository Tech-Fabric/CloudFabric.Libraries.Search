# CloudFabric.Libraries.Search

Search library which abstracts implementation details of search engines.

Supports Azure Search and Elastic search.

Cosists of:

## Attributes

A collection of attributes which can be applied to models and provide search-specific settings like facetable ranges, text analyzers etc.

Designed in engine - independent way and used by all search-engine specific implementations.

## Indexer

Indexer is responsible for creating search indexes on specific search engine. Base indexer provides support methods for registering indexes and reading index configuration.

Indexer implementations should implement index creation method and construct search index based on provided data model and it's attributes.

## Harvester

Harvester is a program which takes records from data storage and sends them to search service. Base harvester interface defines methods for uploading records to search index and removing records from search index.

Harvester implementations implement ISearchUploader interface to upload data objects to search index. Also, it's important for harester to respect model Attributes and proper index selection based on attribute value or another index name provider.

## Filters

A set of classes for constructing engine-independent queries.

## Service

Search service is just an inderface which accepts engine-independent search request with a help of Filters library and returns search results.

Implementations of search service construct engine-specific query from search request, send it to search engine, retrieve results and de-serialize records based on attributes configuration.

![CloudFabric.Libraries.Search](https://github.com/Tech-Fabric/CloudFabric.Libraries.Search/blob/master/TechnicalDiagram.png?raw=true)