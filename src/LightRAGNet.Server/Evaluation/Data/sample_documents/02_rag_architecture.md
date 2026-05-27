# RAG System Architecture

## Main Components of RAG Systems

A RAG system requires three main components: a retrieval system, an embedding model, and a large language model.

## Component 1 Retrieval System

The retrieval system finds relevant documents from large document collections. It can use a vector database or search engine to find documents that match the user query.

## Component 2 Embedding Model

An embedding model converts text into vector representations for similarity search. Documents and queries are embedded into vectors so related content can be matched efficiently.

## Component 3 Large Language Model

A large language model generates responses based on the retrieved context. The LLM uses relevant documents as grounding evidence instead of answering from model memory alone.

## How Components Work Together

Retrieval and embedding work together because embedding models convert documents and queries into vectors, and the retrieval system uses those vector representations to find relevant documents. The LLM then generates the final response from the retrieved context.
