﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Roslyn.Utilities;
using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Reference to metadata stored in the standard ECMA-335 metadata format.
    /// </summary>
    public abstract class PortableExecutableReference : MetadataReference
    {
        private readonly string filePath;

        private DocumentationProvider lazyDocumentation;

        protected PortableExecutableReference(
            MetadataReferenceProperties properties,
            string fullPath = null,
            DocumentationProvider initialDocumentation = null)
            : base(properties)
        {
            this.filePath = fullPath;
            this.lazyDocumentation = initialDocumentation;
        }

        /// <summary>
        /// Display string used in error messages to identity the reference.
        /// </summary>
        public override string Display
        {
            get { return FilePath; }
        }

        /// <summary>
        /// Path describing the location of the metadata, or null if the metadata have no location.
        /// </summary>
        public string FilePath
        {
            get { return filePath; }
        }

        /// <summary>
        /// XML documentation comments provider for the reference.
        /// </summary>
        internal DocumentationProvider DocumentationProvider
        {
            get
            {
                if (lazyDocumentation == null)
                {
                    Interlocked.CompareExchange(ref lazyDocumentation, CreateDocumentationProvider(), null);
                }

                return lazyDocumentation;
            }
        }

        /// <summary>
        /// Create documentation provider for the reference.
        /// </summary>
        /// <remarks>
        /// Called when the compiler needs to read the documentation for the reference. 
        /// This method is called at most once per metadata reference and its result is cached on the reference object.
        /// </remarks>
        protected abstract DocumentationProvider CreateDocumentationProvider();

        /// <summary>
        /// Returns an instance of the reference with specified aliases.
        /// </summary>
        /// <param name="aliases">The new aliases for the reference.</param>
        /// <exception cref="ArgumentException">Alias is invalid for the metadata kind.</exception> 
        public new PortableExecutableReference WithAliases(IEnumerable<string> aliases)
        {
            return this.WithAliases(ImmutableArray.CreateRange(aliases));
        }

        /// <summary>
        /// Returns an instance of the reference with specified aliases.
        /// </summary>
        /// <param name="aliases">The new aliases for the reference.</param>
        /// <exception cref="ArgumentException">Alias is invalid for the metadata kind.</exception> 
        public new PortableExecutableReference WithAliases(ImmutableArray<string> aliases)
        {
            return WithProperties(new MetadataReferenceProperties(this.Properties.Kind, aliases, this.Properties.EmbedInteropTypes));
        }

        /// <summary>
        /// Returns an instance of the reference with specified interop types embedding.
        /// </summary>
        /// <param name="value">The new value for <see cref="MetadataReferenceProperties.EmbedInteropTypes"/>.</param>
        /// <exception cref="ArgumentException">Interop types can't be embedded from modules.</exception> 
        public new PortableExecutableReference WithEmbedInteropTypes(bool value)
        {
            return WithProperties(new MetadataReferenceProperties(this.Properties.Kind, this.Properties.Aliases, value));
        }

        /// <summary>
        /// Returns an instance of the reference with specified properties, or this instance if properties haven't changed.
        /// </summary>
        /// <param name="properties">The new properties for the reference.</param>
        /// <exception cref="ArgumentException">Specified values not valid for this reference.</exception> 
        public new PortableExecutableReference WithProperties(MetadataReferenceProperties properties)
        {
            if (properties == this.Properties)
            {
                return this;
            }

            return WithPropertiesImpl(properties);
        }

        internal sealed override MetadataReference WithPropertiesImplReturningMetadataReference(MetadataReferenceProperties properties)
        {
            return WithPropertiesImpl(properties);
        }

        /// <summary>
        /// Returns an instance of the reference with specified properties.
        /// </summary>
        /// <param name="properties">The new properties for the reference.</param>
        /// <exception cref="NotSupportedException">Specified values not supported.</exception> 
        /// <remarks>Only invoked if the properties changed.</remarks>
        protected abstract PortableExecutableReference WithPropertiesImpl(MetadataReferenceProperties properties);

        /// <summary>
        /// Get metadata representation for the PE file.
        /// </summary>
        /// <exception cref="BadImageFormatException">If the PE image format is invalid.</exception>
        /// <exception cref="IOException">The metadata image content can't be read.</exception>
        /// <exception cref="FileNotFoundException">The metadata image is stored in a file that can't be found.</exception>
        /// <remarks>
        /// Called when the <see cref="Compilation"/> needs to read the reference metadata.
        /// 
        /// The listed exceptions are caught and converted to compilation diagnostics.
        /// Any other exception is considered an unexpected error in the implementation and is not caught.
        ///
        /// <see cref="Metadata"/> objects may cache information decoded from the PE image.
        /// Reusing <see cref="Metadata"/> instances accross metadata references will result in better performance.
        /// 
        /// The calling <see cref="Compilation"/> doesn't take ownership of the <see cref="Metadata"/> objects returned by this method.
        /// The implementation needs to retrieve the object from a provider that manages their lifetime (such as metadata cache).
        /// The <see cref="Metadata"/> object is kept alive by the <see cref="Compilation"/> that called <see cref="GetMetadata"/>
        /// and by all compilations created from it via calls to With- factory methods on <see cref="Compilation"/>, 
        /// other than <see cref="Compilation.WithReferences(MetadataReference[])"/> overloads. A compilation created using 
        /// <see cref="Compilation.WithReferences(MetadataReference[])"/> will call to <see cref="GetMetadata"/> again.
        /// </remarks>
        protected abstract Metadata GetMetadataImpl();

        internal Metadata GetMetadata()
        {
            return GetMetadataImpl();
        }

        internal static Diagnostic ExceptionToDiagnostic(Exception e, CommonMessageProvider messageProvider, Location location, string display, MetadataImageKind kind)
        {
            if (e is BadImageFormatException)
            {
                int errorCode = (kind == MetadataImageKind.Assembly) ? messageProvider.ERR_InvalidAssemblyMetadata : messageProvider.ERR_InvalidModuleMetadata;
                return messageProvider.CreateDiagnostic(errorCode, location, display, e.Message);
            }

            var fileNotFound = e as FileNotFoundException;
            if (fileNotFound != null)
            {
                return messageProvider.CreateDiagnostic(messageProvider.ERR_MetadataFileNotFound, location, fileNotFound.FileName ?? string.Empty);
            }
            else
            {
                int errorCode = (kind == MetadataImageKind.Assembly) ? messageProvider.ERR_ErrorOpeningAssemblyFile : messageProvider.ERR_ErrorOpeningModuleFile;
                return messageProvider.CreateDiagnostic(errorCode, location, display, e.Message);
            }
        }
    }
}